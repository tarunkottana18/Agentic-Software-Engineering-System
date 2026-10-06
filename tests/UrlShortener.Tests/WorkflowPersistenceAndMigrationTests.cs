using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrlShortener.Application.Workflows;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;
using Xunit;

namespace UrlShortener.Tests;

public sealed class WorkflowPersistenceAndMigrationTests
{
    [Fact]
    public async Task Migrations_ApplyCleanlyToFreshDatabase()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"urlshortener-mig-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<UrlDbContext>()
                .UseSqlite($"Data Source={tempDbPath}")
                .Options;

            await using (var context = new UrlDbContext(options))
            {
                await context.Database.MigrateAsync();

                // Verify tables were created by the migration
                var tables = await context.Database
                    .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
                    .ToListAsync();

                Assert.Contains("UrlMappings", tables);
                Assert.Contains("EngineeringWorkflowRuns", tables);
                Assert.Contains("__EFMigrationsHistory", tables);

                context.UrlMappings.Add(new UrlMapping
                {
                    Id = Guid.NewGuid(),
                    OriginalUrl = "https://example.com/migrated",
                    ShortCode = "mig001"
                });
                await context.SaveChangesAsync();
            }

            await using (var verifyContext = new UrlDbContext(options))
            {
                var mapping = await verifyContext.UrlMappings.FirstOrDefaultAsync(u => u.ShortCode == "mig001");
                Assert.NotNull(mapping);
                Assert.Equal("https://example.com/migrated", mapping.OriginalUrl);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(tempDbPath);
        }
    }

    [Fact]
    public async Task Workflow_PersistenceAcrossRestarts()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"urlshortener-restart-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={tempDbPath}";

        Guid workflowId;

        // Session 1: Start API, create workflow, run discovery to AwaitingApproval, then shut down
        using (var factory = new PersistentApiFactory(connectionString))
        {
            using var client = factory.CreateClient();
            await factory.EnsureDatabaseCreatedAsync();

            var create = await client.PostAsJsonAsync(
                "/api/workflows",
                new WorkflowStartRequest("Build a resilient link analyzer service with integration tests."));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
            Assert.NotNull(created);
            workflowId = created.Id;

            // Execute discovery stages
            for (var i = 0; i < 4; i++)
            {
                var wave = await client.PostAsync($"/api/workflows/{workflowId}/execute-ready", null);
                Assert.Equal(HttpStatusCode.OK, wave.StatusCode);
            }

            // Analysis wave
            var analysisWave = await client.PostAsync($"/api/workflows/{workflowId}/execute-ready", null);
            Assert.Equal(HttpStatusCode.OK, analysisWave.StatusCode);

            var view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{workflowId}");
            Assert.NotNull(view);
            var implStage = view.Workflow.Stages.First(s => s.Id == "implementation");
            Assert.Equal(WorkflowStageStatus.AwaitingApproval, implStage.Status);
        } // Process / factory terminates here, closing all connections

        // Session 2: Start brand-new API process against the same persisted database
        using (var restartedFactory = new PersistentApiFactory(connectionString))
        {
            using var restartedClient = restartedFactory.CreateClient();
            restartedClient.DefaultRequestHeaders.Add("X-Approval-Token", "persistent-test-token");

            var reloaded = await restartedClient.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{workflowId}");
            Assert.NotNull(reloaded);
            Assert.Equal(1, reloaded.Workflow.PlanVersion);
            Assert.True(reloaded.Workflow.Events.Count > 5);

            var reloadedImpl = reloaded.Workflow.Stages.First(s => s.Id == "implementation");
            Assert.Equal(WorkflowStageStatus.AwaitingApproval, reloadedImpl.Status);

            // Approve implementation in the restarted session
            var approvalResponse = await restartedClient.PostAsJsonAsync(
                $"/api/workflows/{workflowId}/stages/implementation/approval",
                new WorkflowApprovalRequest("restart-reviewer", true, "Approved across restart."));
            Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);

            var approvedView = await approvalResponse.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
            Assert.NotNull(approvedView);
            Assert.Equal(WorkflowStageStatus.Ready, approvedView.Workflow.Stages.First(s => s.Id == "implementation").Status);
        }

        SqliteConnection.ClearAllPools();
        TryDelete(tempDbPath);
    }

    [Fact]
    public async Task Workflow_OptimisticConcurrency_RejectsStaleRevision()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"urlshortener-concurrency-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<UrlDbContext>()
            .UseSqlite($"Data Source={tempDbPath}")
            .Options;

        try
        {
            await using (var setupContext = new UrlDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                var repo = new EngineeringWorkflowRepository(setupContext);
                var initialRun = new EngineeringWorkflowRun
                {
                    Id = Guid.NewGuid(),
                    Requirement = "Initial concurrency requirement",
                    Status = "Active",
                    PlanVersion = 1,
                    Revision = 1,
                    StateJson = "{}",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await repo.AddAsync(initialRun, CancellationToken.None);
            }

            // Client 1 and Client 2 both read the workflow at Revision 1
            await using var context1 = new UrlDbContext(options);
            await using var context2 = new UrlDbContext(options);

            var repo1 = new EngineeringWorkflowRepository(context1);
            var repo2 = new EngineeringWorkflowRepository(context2);

            var run1 = await repo1.GetByIdAsync(
                (await new UrlDbContext(options).EngineeringWorkflowRuns.SingleAsync()).Id, CancellationToken.None);
            var run2 = await repo2.GetByIdAsync(run1!.Id, CancellationToken.None);

            Assert.NotNull(run1);
            Assert.NotNull(run2);
            Assert.Equal(1, run1.Revision);
            Assert.Equal(1, run2.Revision);

            // Client 1 updates successfully to Revision 2
            run1.Revision = 2;
            run1.Requirement = "Updated by client 1";
            await repo1.UpdateAsync(run1, CancellationToken.None);

            // Client 2 attempts to update to Revision 2 using stale original revision 1
            run2.Revision = 2;
            run2.Requirement = "Updated by client 2 simultaneously";

            var conflictException = await Assert.ThrowsAsync<Core.Exceptions.WorkflowRevisionConflictException>(() =>
                repo2.UpdateAsync(run2, CancellationToken.None));

            Assert.NotNull(conflictException);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(tempDbPath);
        }
    }

    [Fact]
    public async Task Workflow_TwoApiInstances_HandleConcurrentWritesAndConcurrentReads()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"urlshortener-multi-instance-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={tempDbPath};Default Timeout=10";
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"urlshortener-multi-workspace-{Guid.NewGuid():N}");
        try
        {
            using var factory1 = new PersistentApiFactory(connectionString, workspaceRoot: workspaceRoot);
            using var factory2 = new PersistentApiFactory(connectionString, workspaceRoot: workspaceRoot);
            using var client1 = factory1.CreateClient();
            using var client2 = factory2.CreateClient();
            client1.DefaultRequestHeaders.Add("X-Approval-Token", "persistent-test-token");
            client2.DefaultRequestHeaders.Add("X-Approval-Token", "persistent-test-token");
            await factory1.EnsureDatabaseCreatedAsync();
            await factory2.EnsureDatabaseCreatedAsync();

            var id = await CreateReadyWorkflowAsync(client1);
            var beforeRace = await client2.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
            Assert.NotNull(beforeRace);
            Assert.Equal(WorkflowStageStatus.AwaitingApproval, beforeRace.Workflow.Stages.Single(stage => stage.Id == "implementation").Status);

            var starts = await Task.WhenAll(
                client1.PostAsJsonAsync(
                    $"/api/workflows/{id}/stages/implementation/approval",
                    new WorkflowApprovalRequest("multi-instance-reviewer", true, "Approved for concurrency test.")),
                client2.PostAsJsonAsync(
                    $"/api/workflows/{id}/stages/implementation/approval",
                    new WorkflowApprovalRequest("multi-instance-reviewer", true, "Approved for concurrency test.")));
            Assert.Single(starts, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(starts, response => response.StatusCode == HttpStatusCode.Conflict);

            var loads = Enumerable.Range(0, 80)
                .Select(index => (index % 2 == 0 ? client1 : client2).GetAsync($"/api/workflows/{id}"));
            var loadResponses = await Task.WhenAll(loads);
            Assert.All(loadResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(tempDbPath);
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Workflow_Approval_EnforcesConfiguredRole()
    {
        using var factory = new PersistentApiFactory(
            "Data Source=:memory:",
            requiredRole: "ReleaseManager");
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();

        var id = await CreateReadyWorkflowAsync(client);

        // Attempt 1: Valid token but missing X-Approval-Role header -> 403 Forbidden
        client.DefaultRequestHeaders.Add("X-Approval-Token", "persistent-test-token");
        var noRole = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approved without role."));
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);

        // Attempt 2: Valid token but incorrect role -> 403 Forbidden
        client.DefaultRequestHeaders.Add("X-Approval-Role", "Developer");
        var wrongRole = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approved with wrong role."));
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);

        // Attempt 3: Valid token and matching required role -> 200 OK
        client.DefaultRequestHeaders.Remove("X-Approval-Role");
        client.DefaultRequestHeaders.Add("X-Approval-Role", "ReleaseManager");
        var correctRole = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approved with authorized role."));
        Assert.Equal(HttpStatusCode.OK, correctRole.StatusCode);
    }

    private static async Task<Guid> CreateReadyWorkflowAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/workflows",
            new WorkflowStartRequest("Build URL shortener with release validation."));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        var id = view!.Id;

        // Run discovery & analysis waves to reach implementation AwaitingApproval
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        }

        return id;
    }

    private sealed class PersistentApiFactory(
        string connectionString,
        string? requiredRole = null,
        string? workspaceRoot = null) : WebApplicationFactory<Program>
    {
        private SqliteConnection? _memoryConnection;
        private readonly string _workspaceRoot = workspaceRoot ?? Path.Combine(Path.GetTempPath(), "urlshortener-workspace-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var config = new Dictionary<string, string?>
                {
                    ["WorkflowGovernance:ApprovalToken"] = "persistent-test-token",
                    ["WorkflowGovernance:RequiredRole"] = requiredRole,
                    ["WorkflowGovernance:ArtifactRoot"] = Path.Combine(Path.GetTempPath(), "urlshortener-test-art", Guid.NewGuid().ToString("N")),
                    ["WorkflowGovernance:WorkspaceRoot"] = _workspaceRoot
                };
                configuration.AddInMemoryCollection(config);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<UrlDbContext>>();
                services.RemoveAll<UrlDbContext>();

                if (connectionString.Contains(":memory:"))
                {
                    _memoryConnection = new SqliteConnection(connectionString);
                    _memoryConnection.Open();
                    services.AddSingleton(_memoryConnection);
                    services.AddDbContext<UrlDbContext>(options => options.UseSqlite(_memoryConnection));
                }
                else
                {
                    services.AddDbContext<UrlDbContext>(options => options.UseSqlite(connectionString));
                }
            });
        }

        public async Task EnsureDatabaseCreatedAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _memoryConnection?.Dispose();
                if (Directory.Exists(_workspaceRoot))
                {
                    Directory.Delete(_workspaceRoot, recursive: true);
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Ignore lock retention on test teardown
        }
    }
}
