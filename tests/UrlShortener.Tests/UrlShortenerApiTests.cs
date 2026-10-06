using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application.Dtos;
using UrlShortener.Application.Workflows;
using UrlShortener.Infrastructure.Persistence;
using Xunit;

namespace UrlShortener.Tests;

public sealed class UrlShortenerApiTests
{
    [Fact]
    public async Task ShortenThenRedirect_RecordsClickInAnalytics()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await factory.EnsureDatabaseCreatedAsync();

        var shortenResponse = await client.PostAsJsonAsync(
            "/api/url/shorten",
            new UrlShortenRequest("https://example.com/path"));

        Assert.Equal(HttpStatusCode.OK, shortenResponse.StatusCode);
        var shortened = await shortenResponse.Content.ReadFromJsonAsync<UrlShortenResponse>();
        Assert.NotNull(shortened);

        var redirectResponse = await client.GetAsync($"/{shortened.ShortCode}");

        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Equal("https://example.com/path", redirectResponse.Headers.Location?.ToString());

        var analytics = await client.GetFromJsonAsync<UrlAnalyticsResponse>(
            $"/api/url/{shortened.ShortCode}/analytics");

        Assert.NotNull(analytics);
        Assert.Equal(shortened.ShortCode, analytics.ShortCode);
        Assert.Equal(1, analytics.ClickCount);
    }

    [Fact]
    public async Task Shorten_InvalidUrl_ReturnsBadRequest()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();

        var response = await client.PostAsJsonAsync(
            "/api/url/shorten",
            new UrlShortenRequest("ftp://example.com/file"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Workflow_OffersParallelAnalysisAndRequiresApprovalBeforeImplementation()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();

        var create = await client.PostAsJsonAsync(
            "/api/workflows",
            new WorkflowStartRequest("Add a click analytics endpoint with tests and documentation."));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var view = await create.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        var id = view.Id;
        view = await ExecuteDiscoveryStagesAsync(client, id);

        var parallelStageIds = new[]
        {
            "architecture-analysis", "codebase-impact", "ux-api-design", "security-risk-review", "test-strategy"
        };
        Assert.All(parallelStageIds, stageId =>
            Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, stageId).Status));
        Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "implementation").Status);

        foreach (var stageId in parallelStageIds)
        {
            await CompleteStageAsync(client, id, stageId, $"Review completed for {stageId}.");
            if (stageId == parallelStageIds[0])
            {
                view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
                Assert.NotNull(view);
                Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "implementation").Status);
            }
        }

        view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "implementation").Status);

        var prematureStart = await client.PostAsync($"/api/workflows/{id}/stages/implementation/start", null);
        Assert.Equal(HttpStatusCode.Conflict, prematureStart.StatusCode);

        var approval = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest(
                "reviewer@example.test",
                true,
                "Plan reviewed; implementation proposal may proceed.",
                "Implement click analytics with aggregate counts, retention controls, and integration tests."));
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        view = await approval.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "implementation").Status);
        Assert.Equal(
            "Implement click analytics with aggregate counts, retention controls, and integration tests.",
            view.Workflow.Analysis.NormalizedRequirement);
        Assert.Contains(view.Workflow.Events, item => item.Type == "IntentApproved");

        await CompleteStageAsync(client, id, "implementation", "Implementation proposal artifact recorded.");
        view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "test-execution").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "documentation").Status);
        Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "release-readiness").Status);

        await CompleteStageAsync(client, id, "documentation", "Documentation proposal artifact recorded.");
        view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "release-readiness").Status);

        await CompleteStageAsync(client, id, "test-execution", "Test evidence: all required checks passed.");
        Assert.Equal(WorkflowStageStatus.Ready, GetStage((await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}"))!.Workflow, "devops-readiness").Status);
        await CompleteStageAsync(client, id, "devops-readiness", "Infrastructure and deployment readiness reviewed.");
        await CompleteStageAsync(client, id, "release-readiness", "Release checklist reviewed.");
        view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "final-approval").Status);

        var finalApproval = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/final-approval/approval",
            new WorkflowApprovalRequest("release-owner@example.test", true, "Artifacts and validation reviewed."));
        Assert.Equal(HttpStatusCode.OK, finalApproval.StatusCode);
        view = await finalApproval.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowRunStatus.Completed, view.Workflow.Status);
    }

    [Fact]
    public async Task Workflow_ImplementationApproval_RejectsMissingApprovalToken()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Add analytics reporting with validation and tests.");
        await ExecuteDiscoveryStagesAsync(client, id);

        foreach (var stageId in new[]
        {
            "architecture-analysis", "codebase-impact", "ux-api-design", "security-risk-review", "test-strategy"
        })
        {
            await CompleteStageAsync(client, id, stageId, $"Analysis completed for {stageId}.");
        }

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("unverified-reviewer", true, "Approved without token."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Workflow_ExecuteReady_RunsWavesAndPausesForHumanApprovals()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Add click analytics with tests, documentation, and release review.");
        await ExecuteDiscoveryStagesAsync(client, id);

        var firstWave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        Assert.Equal(HttpStatusCode.OK, firstWave.StatusCode);
        var view = await firstWave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.All(new[]
        {
            "architecture-analysis", "codebase-impact", "ux-api-design", "security-risk-review", "test-strategy"
        }, stageId =>
            Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, stageId).Status));
        Assert.Contains("deterministic-local-proposal", GetStage(view.Workflow, "architecture-analysis").Output);
        Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "implementation").Status);

        var implementationApproval = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approve the implementation proposal stage."));
        Assert.Equal(HttpStatusCode.OK, implementationApproval.StatusCode);

        var implementationWave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        Assert.Equal(HttpStatusCode.OK, implementationWave.StatusCode);
        view = await implementationWave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, "implementation").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "test-execution").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "documentation").Status);

        var validationWave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        Assert.Equal(HttpStatusCode.OK, validationWave.StatusCode);
        view = await validationWave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, "test-execution").Status);
        Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, "documentation").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "devops-readiness").Status);

        var devopsWave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        Assert.Equal(HttpStatusCode.OK, devopsWave.StatusCode);
        view = await devopsWave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, "devops-readiness").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "release-readiness").Status);

        var releaseWave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        Assert.Equal(HttpStatusCode.OK, releaseWave.StatusCode);
        view = await releaseWave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.Completed, GetStage(view.Workflow, "release-readiness").Status);
        Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "final-approval").Status);
    }

    [Fact]
    public async Task Workflow_Greenfield_MarksCodebaseImpactNotApplicableAndStillUnlocksImplementation()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Build a new URL-shortening endpoint with tests and setup documentation.");
        await ExecuteDiscoveryStagesAsync(client, id);

        var view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(view);
        var impact = GetStage(view.Workflow, "codebase-impact");
        Assert.Equal(WorkflowStageStatus.Completed, impact.Status);
        Assert.StartsWith("Not applicable", impact.Output);
        Assert.Contains(view.Workflow.Events, item => item.Type == "StageNotApplicable");

        var wave = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
        view = await wave.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "implementation").Status);
    }

    [Fact]
    public async Task Workflow_GreenfieldImplementation_RejectsArtifactsOutsideApprovedPath()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Build a new URL-shortening endpoint with tests and setup documentation.");
        await ExecuteDiscoveryStagesAsync(client, id);
        (await client.PostAsync($"/api/workflows/{id}/execute-ready", null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approved."))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/workflows/{id}/stages/implementation/start", null)).EnsureSuccessStatusCode();

        var outside = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/result",
            new WorkflowStageResultRequest(true, "Scaffold created.", ChangedFiles: ["src/UrlShortener.Api/Program.cs"]));
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);

        var traversal = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/result",
            new WorkflowStageResultRequest(true, "Scaffold created.", ChangedFiles: ["generated/greenfield/../src"]));
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);

        var generatedFile = Path.Combine(factory.WorkspaceRoot, "generated", "greenfield", "url-shortener-demo", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(generatedFile)!);
        await File.WriteAllTextAsync(generatedFile, "public static class Program { }");
        var inside = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/result",
            new WorkflowStageResultRequest(true, "Scaffold created.", ChangedFiles: ["generated/greenfield/url-shortener-demo/Program.cs"]));
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
    }

    [Fact]
    public async Task Workflow_ImplementationResult_VerifiesActualWorkspaceChangesAndHashes()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();
        var existingFile = Path.Combine(factory.WorkspaceRoot, "src", "UrlShortener.Api", "Program.cs");
        var deletedFile = Path.Combine(factory.WorkspaceRoot, "src", "UrlShortener.Api", "Legacy.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(existingFile)!);
        await File.WriteAllTextAsync(existingFile, "// baseline");
        await File.WriteAllTextAsync(deletedFile, "// removed during implementation");
        var id = await CreateWorkflowAsync(client, "Add click analytics to the existing short-link redirect and update regression tests.");
        await ExecuteDiscoveryStagesAsync(client, id);
        (await client.PostAsync($"/api/workflows/{id}/execute-ready", null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/approval",
            new WorkflowApprovalRequest("reviewer", true, "Approved."))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/workflows/{id}/stages/implementation/start", null)).EnsureSuccessStatusCode();

        await File.WriteAllTextAsync(existingFile, "// implementation change");
        File.Delete(deletedFile);
        var addedFile = Path.Combine(factory.WorkspaceRoot, "tests", "UrlShortener.Tests", "RedirectTests.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(addedFile)!);
        await File.WriteAllTextAsync(addedFile, "// added regression test");

        var omittedChange = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/result",
            new WorkflowStageResultRequest(true, "Implementation complete."));
        Assert.Equal(HttpStatusCode.BadRequest, omittedChange.StatusCode);

        var verified = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/implementation/result",
            new WorkflowStageResultRequest(true, "Implementation complete.", ChangedFiles:
            [
                "src/UrlShortener.Api/Program.cs",
                "src/UrlShortener.Api/Legacy.cs",
                "tests/UrlShortener.Tests/RedirectTests.cs"
            ]));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var view = await verified.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(3, view.Workflow.ImplementationChanges.Count);
        var modified = Assert.Single(view.Workflow.ImplementationChanges, change => change.Path == "src/UrlShortener.Api/Program.cs");
        Assert.Matches("^[A-F0-9]{64}$", modified.BaselineSha256);
        Assert.Matches("^[A-F0-9]{64}$", modified.CurrentSha256);
        var deleted = Assert.Single(view.Workflow.ImplementationChanges, change => change.Path == "src/UrlShortener.Api/Legacy.cs");
        Assert.Matches("^[A-F0-9]{64}$", deleted.BaselineSha256);
        Assert.Null(deleted.CurrentSha256);
        var added = Assert.Single(view.Workflow.ImplementationChanges, change => change.Path == "tests/UrlShortener.Tests/RedirectTests.cs");
        Assert.Null(added.BaselineSha256);
        Assert.Matches("^[A-F0-9]{64}$", added.CurrentSha256);
        Assert.Contains(view.Workflow.Events, item => item.Type == "ImplementationFilesVerified");
    }

    [Fact]
    public async Task Workflow_StopsAfterBoundedRetriesAndExposesMetrics()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Add a measurable click analytics endpoint with tests.");
        await ExecuteDiscoveryStagesAsync(client, id);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var start = await client.PostAsync($"/api/workflows/{id}/stages/architecture-analysis/start", null);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            var result = await client.PostAsJsonAsync(
                $"/api/workflows/{id}/stages/architecture-analysis/result",
                new WorkflowStageResultRequest(false, string.Empty, "Analysis tool failed."));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        var workflow = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowRunStatus.SafeStopped, workflow.Workflow.Status);
        Assert.Equal(2, workflow.Workflow.Events.Count(item => item.Type == "RetryScheduled"));

        var metrics = await client.GetFromJsonAsync<WorkflowMetrics>("/api/workflows/metrics");
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.TotalWorkflows);
        Assert.Equal(0, metrics.SuccessRate);
        Assert.Equal(2, metrics.RetryCount);
    }

    [Fact]
    public async Task Workflow_ReplanAndRollback_PreservePlanLineage()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Approval-Token", "test-approval-token");
        await factory.EnsureDatabaseCreatedAsync();
        var originalRequirement = "Add click analytics reporting to shortened URLs.";
        var id = await CreateWorkflowAsync(client, originalRequirement);

        var replan = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/replan",
            new WorkflowPlanRequest("Add click analytics with a seven-day retention requirement."));
        Assert.Equal(HttpStatusCode.OK, replan.StatusCode);
        var replanned = await replan.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(replanned);
        Assert.Equal(2, replanned.Workflow.PlanVersion);
        Assert.Single(replanned.Workflow.PreviousPlans);

        var rollback = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/rollback",
            new WorkflowRollbackRequest("reviewer@example.test", "The retention assumption was not approved."));
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        var rolledBack = await rollback.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(rolledBack);
        Assert.Equal(3, rolledBack.Workflow.PlanVersion);
        Assert.Equal(originalRequirement, rolledBack.Workflow.Requirement);
        Assert.Contains(rolledBack.Workflow.Events, item => item.Type == "PlanRolledBack");
    }

    [Fact]
    public async Task Workflow_ChangedUpstreamOutput_ReplansDependentStages()
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();
        var id = await CreateWorkflowAsync(client, "Add click analytics and retain only aggregate counts.");
        await ExecuteDiscoveryStagesAsync(client, id);

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/task-decomposition/replan",
            new WorkflowStageReplanRequest(
                "Revised tasks include an analytics query, retention policy, and integration tests.",
                "The original task breakdown omitted retention requirements."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        Assert.Equal(2, view.Workflow.PlanVersion);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "architecture-analysis").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "codebase-impact").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "ux-api-design").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "security-risk-review").Status);
        Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "test-strategy").Status);
        Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "implementation").Status);
        Assert.Contains(view.Workflow.PreviousPlans.Single().Stages, stage =>
            stage.Id == "task-decomposition" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(view.Workflow.Events, item => item.Type == "UpstreamOutputChanged");
    }

    [Theory]
    [InlineData("Greenfield", "Build a new URL-shortening endpoint with tests and setup documentation.", false, "Low", "Greenfield")]
    [InlineData("Brownfield", "Add click analytics to the existing short-link redirect and update regression tests.", false, "Low", "Brownfield")]
    [InlineData("Ambiguous", "Improve it", true, "Medium", "Ambiguous")]
    [InlineData("Ambiguous", "Make shared links safer.", true, "Medium", "Ambiguous")]
    public async Task Workflow_ScenariosProducePlanAndRecordClarification(
        string scenario,
        string requirement,
        bool expectsClarification,
        string expectedRisk,
        string expectedScenarioType)
    {
        using var factory = new UrlShortenerApiFactory();
        using var client = factory.CreateClient();
        await factory.EnsureDatabaseCreatedAsync();

        var id = await CreateWorkflowAsync(client, requirement);
        var view = await ExecuteDiscoveryStagesAsync(client, id);

        Assert.NotNull(view);
        Assert.Equal(expectedRisk, view.Workflow.Analysis.RiskLevel);
        Assert.Equal(expectedScenarioType, view.Workflow.Analysis.ScenarioType);
        Assert.Equal(expectsClarification, view.Workflow.Analysis.ClarificationQuestions.Count > 0);
        Assert.Contains(view.Workflow.Stages, stage => stage.Id == "codebase-impact");
        Assert.Contains(view.Workflow.Stages, stage => stage.Id == "test-strategy");
        Assert.Contains(view.Workflow.Analysis.Tasks, task => !string.IsNullOrWhiteSpace(task.Title));
        if (scenario == "Ambiguous")
        {
            Assert.Contains(view.Workflow.Events, item => item.Type == "ClarificationRequired");
            Assert.Equal(WorkflowStageStatus.AwaitingApproval, GetStage(view.Workflow, "clarification").Status);
            Assert.Equal(WorkflowStageStatus.Blocked, GetStage(view.Workflow, "architecture-analysis").Status);
        }
        else
        {
            Assert.DoesNotContain(view.Workflow.Stages, stage => stage.Id == "clarification");
            Assert.Equal(WorkflowStageStatus.Ready, GetStage(view.Workflow, "architecture-analysis").Status);
        }
    }

    private static async Task<EngineeringWorkflowView> ExecuteDiscoveryStagesAsync(HttpClient client, Guid id)
    {
        EngineeringWorkflowView? view = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            view = await client.GetFromJsonAsync<EngineeringWorkflowView>($"/api/workflows/{id}");
            Assert.NotNull(view);
            if (GetStage(view.Workflow, "task-decomposition").Status == WorkflowStageStatus.Completed)
            {
                return view;
            }

            var response = await client.PostAsync($"/api/workflows/{id}/execute-ready", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            view = await response.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
            Assert.NotNull(view);
        }

        throw new Xunit.Sdk.XunitException("Discovery stages did not complete within five sequential steps.");
    }

    private static WorkflowStage GetStage(EngineeringWorkflowState workflow, string stageId)
    {
        return Assert.Single(workflow.Stages, stage => stage.Id == stageId);
    }

    private static async Task<Guid> CreateWorkflowAsync(HttpClient client, string requirement)
    {
        var response = await client.PostAsJsonAsync("/api/workflows", new WorkflowStartRequest(requirement));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<EngineeringWorkflowView>();
        Assert.NotNull(view);
        return view.Id;
    }

    private static async Task CompleteStageAsync(HttpClient client, Guid id, string stageId, string output)
    {
        var start = await client.PostAsync($"/api/workflows/{id}/stages/{stageId}/start", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var result = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/stages/{stageId}/result",
            new WorkflowStageResultRequest(true, output));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    private sealed class UrlShortenerApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "urlshortener-workspace-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _connection.Open();
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["WorkflowGovernance:ApprovalToken"] = "test-approval-token",
                    ["WorkflowGovernance:ArtifactRoot"] = Path.Combine(Path.GetTempPath(), "urlshortener-tests", Guid.NewGuid().ToString("N")),
                    ["WorkflowGovernance:WorkspaceRoot"] = WorkspaceRoot
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<UrlDbContext>>();
                services.RemoveAll<UrlDbContext>();
                services.AddSingleton(_connection);
                services.AddDbContext<UrlDbContext>(options => options.UseSqlite(_connection));
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
                _connection.Dispose();
                if (Directory.Exists(WorkspaceRoot))
                {
                    Directory.Delete(WorkspaceRoot, recursive: true);
                }
            }
        }
    }
}