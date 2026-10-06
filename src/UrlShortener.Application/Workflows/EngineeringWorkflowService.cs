using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Workflows;

public sealed class EngineeringWorkflowService : IEngineeringWorkflowService
{
    private const int MaximumRequirementLength = 4000;
    private const int MaximumOutputLength = 30000;
    private const int MaximumAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IEngineeringWorkflowRepository _repository;
    private readonly IRequirementAnalyzer _analyzer;
    private readonly IWorkflowStageExecutor _stageExecutor;
    private readonly IWorkflowArtifactStore _artifactStore;
    private readonly IWorkspaceChangeTracker _workspaceChangeTracker;

    public EngineeringWorkflowService(
        IEngineeringWorkflowRepository repository,
        IRequirementAnalyzer analyzer,
        IWorkflowStageExecutor stageExecutor,
        IWorkflowArtifactStore artifactStore,
        IWorkspaceChangeTracker workspaceChangeTracker)
    {
        _repository = repository;
        _analyzer = analyzer;
        _stageExecutor = stageExecutor;
        _artifactStore = artifactStore;
        _workspaceChangeTracker = workspaceChangeTracker;
    }

    public async Task<EngineeringWorkflowView> CreateAsync(
        WorkflowStartRequest request,
        CancellationToken cancellationToken)
    {
        var requirement = request.Requirement?.Trim();
        if (string.IsNullOrWhiteSpace(requirement) || requirement.Length > MaximumRequirementLength)
        {
            throw new WorkflowValidationException($"Requirement must contain 1 to {MaximumRequirementLength} characters.");
        }

        var analysis = await _analyzer.AnalyzeAsync(requirement, cancellationToken);
        var now = DateTime.UtcNow;
        var state = new EngineeringWorkflowState
        {
            Requirement = requirement,
            Analysis = analysis,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Stages = BuildStages(analysis),
            Events = []
        };

        AddEvent(state, "WorkflowCreated", null, "system", "Requirement analyzed and initial plan created.");
        foreach (var question in analysis.ClarificationQuestions)
        {
            AddEvent(state, "ClarificationRequired", "requirement-analysis", "agent", question);
        }

        foreach (var assumption in analysis.Assumptions)
        {
            AddEvent(state, "AssumptionRecorded", "requirement-analysis", "agent", assumption);
        }

        if (analysis.PlanningSource.StartsWith("local-fallback", StringComparison.Ordinal))
        {
            AddEvent(state, "PlanningFallback", "requirement-analysis", "system", analysis.PlanningSource);
        }

        RefreshReadiness(state);
        var run = CreateRun(state);
        await _repository.AddAsync(run, cancellationToken);
        return ToView(run, state);
    }

    public async Task<EngineeringWorkflowView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await _repository.GetByIdAsync(id, cancellationToken);
        return run is null ? null : ToView(run, Deserialize(run));
    }

    public async Task<IReadOnlyList<EngineeringWorkflowView>> GetAllAsync(CancellationToken cancellationToken)
    {
        var runs = await _repository.GetAllAsync(cancellationToken);
        return runs.Select(run => ToView(run, Deserialize(run))).ToArray();
    }

    public async Task<EngineeringWorkflowView> StartStageAsync(
        Guid id,
        string stageId,
        CancellationToken cancellationToken)
    {
        var (run, state) = await LoadAsync(id, cancellationToken);
        var stage = FindStage(state, stageId);
        if (stage.Status != WorkflowStageStatus.Ready)
        {
            throw new WorkflowConflictException($"Stage '{stageId}' is not ready to start.");
        }

        if (stage.Attempts >= stage.MaxAttempts)
        {
            throw new WorkflowConflictException($"Stage '{stageId}' has reached its attempt limit.");
        }

        ReplaceStage(state, stage with
        {
            Status = WorkflowStageStatus.Running,
            Attempts = stage.Attempts + 1,
            FailureReason = null
        });
        AddEvent(state, "StageStarted", stageId, "agent", $"Started attempt {stage.Attempts + 1} of {stage.MaxAttempts}.");
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<EngineeringWorkflowView> ExecuteReadyStagesAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, initialState) = await LoadAsync(id, cancellationToken);
        var readyStages = initialState.Stages
            .Where(stage => stage.Status == WorkflowStageStatus.Ready
                && (!stage.RequiresApproval || initialState.Approvals.Any(approval =>
                    approval.StageId == stage.Id && approval.PlanVersion == initialState.PlanVersion && approval.Approved)))
            .ToArray();
        if (readyStages.Length == 0)
        {
            throw new WorkflowConflictException("There are no executable ready stages. A human approval or prerequisite result may be required.");
        }

        foreach (var stage in readyStages)
        {
            await StartStageAsync(id, stage.Id, cancellationToken);
        }

        var results = await Task.WhenAll(readyStages.Select(async stage =>
        {
            try
            {
                return (stage.Id, Result: await _stageExecutor.ExecuteAsync(initialState, stage, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                return (stage.Id, Result: new WorkflowStageExecutionResult(false, string.Empty, "Stage execution was cancelled."));
            }
            catch (Exception exception)
            {
                return (stage.Id, Result: new WorkflowStageExecutionResult(false, string.Empty, Truncate(exception.Message, 2000)));
            }
        }));

        EngineeringWorkflowView? latest = null;
        foreach (var (stageId, result) in results)
        {
            latest = await SubmitStageResultAsync(
                id,
                stageId,
                new WorkflowStageResultRequest(result.Succeeded, result.Output, result.FailureReason, result.ArtifactReferences),
                CancellationToken.None);
        }

        return latest!;
    }

    public async Task<EngineeringWorkflowView> SubmitStageResultAsync(
        Guid id,
        string stageId,
        WorkflowStageResultRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Output is null || request.Output.Length > MaximumOutputLength)
        {
            throw new WorkflowValidationException($"Stage output must be at most {MaximumOutputLength} characters.");
        }

        if (request.ArtifactReferences is { Count: > 20 }
            || request.ArtifactReferences?.Any(reference => reference.Length > 512) == true
            || request.ChangedFiles is { Count: > 1000 }
            || request.ChangedFiles?.Any(path => path.Length > 512) == true)
        {
            throw new WorkflowValidationException("Submit no more than 20 artifact references and 1000 changed files; each path must be at most 512 characters.");
        }

        var (run, state) = await LoadAsync(id, cancellationToken);
        var stage = FindStage(state, stageId);
        if (stage.Status != WorkflowStageStatus.Running)
        {
            throw new WorkflowConflictException($"Stage '{stageId}' must be started before submitting a result.");
        }

        if (stageId == "implementation" && state.Analysis.ScenarioType == "Greenfield")
        {
            EnsureGreenfieldPaths(request.ChangedFiles);
        }
        if (stageId == "implementation" && request.Succeeded)
        {
            state.ImplementationChanges = (await _workspaceChangeTracker.FindChangesAsync(
                state.ImplementationBaseline, cancellationToken)).ToList();
            EnsureReportedChangesMatch(state.ImplementationChanges, request.ChangedFiles);
            if (state.Analysis.ScenarioType == "Brownfield"
                && !state.ImplementationChanges.Any(change =>
                    change.Path.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                    || change.Path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new WorkflowValidationException("A successful Brownfield implementation must include at least one verified source or test change.");
            }

            var manifest = JsonSerializer.Serialize(state.ImplementationChanges, JsonOptions);
            var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
            AddEvent(state, "ImplementationFilesVerified", stageId, "system",
                $"Verified {state.ImplementationChanges.Count} changed files. Manifest SHA-256: {manifestHash}.");
        }
        if (request.Succeeded)
        {
            if (string.IsNullOrWhiteSpace(request.Output))
            {
                throw new WorkflowValidationException("A successful stage must include its output.");
            }

            var artifactPath = await _artifactStore.WriteStageArtifactAsync(
                id,
                state.PlanVersion,
                stage,
                request.Output,
                cancellationToken);
            var artifactReferences = (request.ArtifactReferences ?? [])
                .Append(artifactPath)
                .Take(20)
                .ToArray();
            ReplaceStage(state, stage with
            {
                Status = WorkflowStageStatus.Completed,
                Output = request.Output,
                FailureReason = null,
                ArtifactReferences = artifactReferences
            });
            var outputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Output)));
            var artifactSummary = string.Join(", ", artifactReferences);
            AddEvent(state, "StageCompleted", stageId, "agent", $"Output SHA-256: {outputHash}; artifacts: {artifactSummary}");
        }
        else
        {
            var failureReason = string.IsNullOrWhiteSpace(request.FailureReason)
                ? "Stage reported an unspecified failure."
                : Truncate(request.FailureReason.Trim(), 2000);
            AddEvent(state, "StageFailed", stageId, "agent", failureReason);

            if (stage.Attempts < stage.MaxAttempts)
            {
                ReplaceStage(state, stage with
                {
                    Status = WorkflowStageStatus.Ready,
                    FailureReason = failureReason
                });
                AddEvent(state, "RetryScheduled", stageId, "system", "A bounded retry is available; revise the attempt using the failure details.");
            }
            else
            {
                ReplaceStage(state, stage with
                {
                    Status = WorkflowStageStatus.Skipped,
                    FailureReason = failureReason
                });
                SafeStop(state, stageId, "Attempt limit reached. Downstream work was stopped; a human must re-plan or roll back the plan.");
            }
        }

        RefreshReadiness(state);
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<EngineeringWorkflowView> ApproveStageAsync(
        Guid id,
        string stageId,
        WorkflowApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Approver) || string.IsNullOrWhiteSpace(request.Rationale))
        {
            throw new WorkflowValidationException("Approval requires an approver name and rationale.");
        }

        var (run, state) = await LoadAsync(id, cancellationToken);
        var stage = FindStage(state, stageId);
        if (!stage.RequiresApproval || stage.Status != WorkflowStageStatus.AwaitingApproval)
        {
            throw new WorkflowConflictException($"Stage '{stageId}' is not awaiting human approval.");
        }

        state.Approvals.Add(new WorkflowApproval(
            stageId,
            request.Approver.Trim(),
            request.Approved,
            request.Rationale.Trim(),
            state.PlanVersion,
            DateTime.UtcNow));

        if (!request.Approved)
        {
            ReplaceStage(state, stage with { Status = WorkflowStageStatus.Skipped, FailureReason = request.Rationale.Trim() });
            AddEvent(state, "ApprovalRejected", stageId, request.Approver.Trim(), request.Rationale.Trim());
            SafeStop(state, stageId, "Human approval was denied. No dependent work may proceed.");
        }
        else if (stageId == "final-approval")
        {
            ReplaceStage(state, stage with { Status = WorkflowStageStatus.Completed, Output = request.Rationale.Trim() });
            AddEvent(state, "FinalApprovalGranted", stageId, request.Approver.Trim(), request.Rationale.Trim());
            state.Status = WorkflowRunStatus.Completed;
            state.CompletedAtUtc = DateTime.UtcNow;
        }
        else
        {
            ReplaceStage(state, stage with { Status = WorkflowStageStatus.Ready, Output = request.Rationale.Trim() });
            AddEvent(state, "ApprovalGranted", stageId, request.Approver.Trim(), request.Rationale.Trim());
            if (stageId == "implementation" && !string.IsNullOrWhiteSpace(request.ApprovedIntent))
            {
                var approvedIntent = request.ApprovedIntent.Trim();
                state.Analysis = state.Analysis with { NormalizedRequirement = approvedIntent };
                var intentStage = FindStage(state, "intent-analysis");
                ReplaceStage(state, intentStage with { Output = approvedIntent });
                AddEvent(state, "IntentApproved", stageId, request.Approver.Trim(), approvedIntent);
            }
            if (stageId == "implementation")
            {
                state.ImplementationBaseline = (await _workspaceChangeTracker.CaptureAsync(cancellationToken)).ToList();
                state.ImplementationChanges = [];
                AddEvent(state, "ImplementationBaselineCaptured", stageId, request.Approver.Trim(),
                    $"Captured {state.ImplementationBaseline.Count} workspace file hashes before implementation.");
            }
            state.Status = WorkflowRunStatus.Active;
        }

        RefreshReadiness(state);
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<EngineeringWorkflowView> ReplanAsync(
        Guid id,
        WorkflowPlanRequest request,
        CancellationToken cancellationToken)
    {
        var requirement = request.Requirement?.Trim();
        if (string.IsNullOrWhiteSpace(requirement) || requirement.Length > MaximumRequirementLength)
        {
            throw new WorkflowValidationException($"Requirement must contain 1 to {MaximumRequirementLength} characters.");
        }

        var (run, state) = await LoadAsync(id, cancellationToken);
        EnsureCanReplan(state);
        var analysis = await _analyzer.AnalyzeAsync(requirement, cancellationToken);
        PreserveCurrentPlan(state);
        state.PlanVersion++;
        state.Requirement = requirement;
        state.Analysis = analysis;
        state.Stages = BuildStages(analysis);
        state.Status = WorkflowRunStatus.Active;
        state.CompletedAtUtc = null;
        AddEvent(state, "PlanReplanned", null, "human", "Requirement changed; a new plan version replaced downstream stage state.");
        RefreshReadiness(state);
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<EngineeringWorkflowView> RollbackPlanAsync(
        Guid id,
        WorkflowRollbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Approver) || string.IsNullOrWhiteSpace(request.Rationale))
        {
            throw new WorkflowValidationException("Plan rollback requires an approver name and rationale.");
        }

        var (run, state) = await LoadAsync(id, cancellationToken);
        EnsureCanReplan(state);
        if (state.PreviousPlans.Count == 0)
        {
            throw new WorkflowConflictException("There is no earlier plan version to roll back to.");
        }

        var previousPlan = state.PreviousPlans[^1];
        await _artifactStore.DeletePlanArtifactsAsync(id, state.PlanVersion, cancellationToken);
        PreserveCurrentPlan(state);
        state.PlanVersion++;
        state.Requirement = previousPlan.Requirement;
        state.Analysis = previousPlan.Analysis;
        state.Stages = previousPlan.Stages
            .Select(stage => stage.Id is "implementation" or "test-execution" or "documentation" or "devops-readiness" or "release-readiness" or "final-approval"
                ? stage with
                {
                    Status = WorkflowStageStatus.Blocked,
                    Attempts = 0,
                    Output = null,
                    FailureReason = null,
                    ArtifactReferences = []
                }
                : stage)
            .ToList();
        state.Status = WorkflowRunStatus.Active;
        state.CompletedAtUtc = null;
        AddEvent(state, "PlanRolledBack", null, request.Approver.Trim(), request.Rationale.Trim());
        RefreshReadiness(state);
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<EngineeringWorkflowView> ReplanFromStageAsync(
        Guid id,
        string stageId,
        WorkflowStageReplanRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RevisedOutput)
            || request.RevisedOutput.Length > MaximumOutputLength
            || string.IsNullOrWhiteSpace(request.Rationale))
        {
            throw new WorkflowValidationException("A revised stage output (within the output limit) and rationale are required.");
        }

        var (run, state) = await LoadAsync(id, cancellationToken);
        EnsureCanReplan(state);
        var changedStage = FindStage(state, stageId);
        if (changedStage.Status != WorkflowStageStatus.Completed)
        {
            throw new WorkflowConflictException("Only a completed upstream stage can trigger output-based re-planning.");
        }

        PreserveCurrentPlan(state);
        state.PlanVersion++;
        ReplaceStage(state, changedStage with
        {
            Status = changedStage.RequiresApproval ? WorkflowStageStatus.AwaitingApproval : WorkflowStageStatus.Completed,
            Output = request.RevisedOutput,
            FailureReason = null,
            Attempts = 0
        });

        var affectedStageIds = FindDescendants(state.Stages, stageId);
        if (state.Analysis.ScenarioType != "Ambiguous" && state.Analysis.ClarificationQuestions.Count == 0)
        {
            affectedStageIds.Remove("clarification");
        }

        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            if (affectedStageIds.Contains(stage.Id))
            {
                state.Stages[index] = stage with
                {
                    Status = WorkflowStageStatus.Blocked,
                    Attempts = 0,
                    Output = null,
                    FailureReason = null,
                    ArtifactReferences = []
                };
            }
        }

        var revisedOutputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RevisedOutput)));
        AddEvent(state, "UpstreamOutputChanged", stageId, "human", $"{Truncate(request.Rationale.Trim(), 1000)} Output SHA-256: {revisedOutputHash}.");
        AddEvent(state, "PlanReplanned", stageId, "system", $"Plan version {state.PlanVersion} invalidated downstream stages: {string.Join(", ", affectedStageIds)}.");
        state.Status = WorkflowRunStatus.Active;
        state.CompletedAtUtc = null;
        RefreshReadiness(state);
        return await SaveAsync(run, state, cancellationToken);
    }

    public async Task<WorkflowMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var runs = await _repository.GetAllAsync(cancellationToken);
        var states = runs.Select(run => (Run: run, State: Deserialize(run))).ToArray();
        var terminal = states.Where(item => item.State.Status is WorkflowRunStatus.Completed or WorkflowRunStatus.SafeStopped).ToArray();
        var completed = terminal.Count(item => item.State.Status == WorkflowRunStatus.Completed);
        var mttrSamples = states.SelectMany(item => CalculateMttrSamples(item.State)).ToArray();
        var latencies = terminal
            .Where(item => item.State.CompletedAtUtc.HasValue)
            .Select(item => (item.State.CompletedAtUtc!.Value - item.Run.CreatedAtUtc).TotalSeconds)
            .ToArray();

        return new WorkflowMetrics(
            states.Length,
            terminal.Length,
            terminal.Length == 0 ? 0 : (double)completed / terminal.Length,
            states.Sum(item => item.State.Events.Count(workflowEvent => workflowEvent.Type == "RetryScheduled")),
            states.Sum(item => item.State.Events.Count(workflowEvent => workflowEvent.Type == "PlanRolledBack")),
            mttrSamples.Length == 0 ? 0 : mttrSamples.Average(),
            latencies.Length == 0 ? 0 : latencies.Average());
    }

    private async Task<(EngineeringWorkflowRun Run, EngineeringWorkflowState State)> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new WorkflowNotFoundException(id);
        return (run, Deserialize(run));
    }

    private async Task<EngineeringWorkflowView> SaveAsync(
        EngineeringWorkflowRun run,
        EngineeringWorkflowState state,
        CancellationToken cancellationToken)
    {
        state.UpdatedAtUtc = DateTime.UtcNow;
        run.Status = state.Status.ToString();
        run.Requirement = state.Requirement;
        run.PlanVersion = state.PlanVersion;
        run.Revision++;
        run.StateJson = JsonSerializer.Serialize(state, JsonOptions);
        run.UpdatedAtUtc = state.UpdatedAtUtc;

        try
        {
            await _repository.UpdateAsync(run, cancellationToken);
        }
        catch (WorkflowRevisionConflictException exception)
        {
            throw new WorkflowConflictException("Workflow changed concurrently. Reload it and retry using its latest state.", exception);
        }

        return ToView(run, state);
    }

    private static EngineeringWorkflowRun CreateRun(EngineeringWorkflowState state)
    {
        var id = Guid.NewGuid();
        return new EngineeringWorkflowRun
        {
            Id = id,
            Requirement = state.Requirement,
            Status = state.Status.ToString(),
            PlanVersion = state.PlanVersion,
            Revision = 1,
            StateJson = JsonSerializer.Serialize(state, JsonOptions),
            CreatedAtUtc = state.CreatedAtUtc,
            UpdatedAtUtc = state.UpdatedAtUtc
        };
    }

    private static EngineeringWorkflowState Deserialize(EngineeringWorkflowRun run)
    {
        return JsonSerializer.Deserialize<EngineeringWorkflowState>(run.StateJson, JsonOptions)
            ?? throw new InvalidOperationException($"Workflow {run.Id} has invalid persisted state.");
    }

    private static EngineeringWorkflowView ToView(EngineeringWorkflowRun run, EngineeringWorkflowState state)
    {
        return new EngineeringWorkflowView(run.Id, run.Revision, state);
    }

    private static List<WorkflowStage> BuildStages(RequirementAnalysis analysis)
    {
        var requiresClarification = analysis.ScenarioType == "Ambiguous" || analysis.ClarificationQuestions.Count > 0;
        var downstreamDependency = requiresClarification ? "clarification" : "task-decomposition";
        var stages = new List<WorkflowStage>
        {
            Stage("requirement-analysis", "Requirement understanding", "requirements", [], WorkflowStageStatus.Completed,
                output: analysis.NormalizedRequirement + Environment.NewLine + "Risk: " + analysis.RiskLevel),
            Stage("product-purpose", "Product purpose and outcomes", "discovery", ["requirement-analysis"], WorkflowStageStatus.Ready),
            Stage("persona-research", "Persona and pressure context", "discovery", ["product-purpose"], WorkflowStageStatus.Blocked),
            Stage("intent-analysis", "User intents and capabilities", "discovery", ["persona-research"], WorkflowStageStatus.Blocked),
            Stage("task-decomposition", "Task decomposition", "planning", ["intent-analysis"], WorkflowStageStatus.Blocked)
        };

        if (requiresClarification)
        {
            stages.Add(Stage(
                "clarification",
                "Human clarification",
                "requirements",
                ["task-decomposition"],
                WorkflowStageStatus.Blocked,
                output: string.Join(Environment.NewLine, analysis.ClarificationQuestions),
                requiresApproval: true));
        }

        stages.AddRange(
        [
            Stage("architecture-analysis", "Architecture/design analysis", "design", [downstreamDependency], WorkflowStageStatus.Blocked),
            Stage("codebase-impact", "Codebase impact analysis", "brownfield", [downstreamDependency], WorkflowStageStatus.Blocked),
            Stage("ux-api-design", "UX and API design", "ux", [downstreamDependency], WorkflowStageStatus.Blocked),
            Stage("security-risk-review", "Security and risk review", "security", [downstreamDependency], WorkflowStageStatus.Blocked),
            Stage("test-strategy", "Test strategy", "testing", [downstreamDependency], WorkflowStageStatus.Blocked),
            Stage("implementation", "Implementation proposal", "implementation", ["architecture-analysis", "codebase-impact", "ux-api-design", "security-risk-review", "test-strategy"], WorkflowStageStatus.Blocked, requiresApproval: true),
            Stage("test-execution", "Run and review tests", "validation", ["implementation", "test-strategy"], WorkflowStageStatus.Blocked),
            Stage("documentation", "Update documentation", "documentation", ["implementation"], WorkflowStageStatus.Blocked),
            Stage("devops-readiness", "DevOps and deployment readiness", "devops", ["test-execution", "documentation", "security-risk-review"], WorkflowStageStatus.Blocked),
            Stage("release-readiness", "Release readiness review", "release", ["devops-readiness"], WorkflowStageStatus.Blocked),
            Stage("final-approval", "Final human review", "approval", ["release-readiness"], WorkflowStageStatus.Blocked, requiresApproval: true)
        ]);
        return stages;
    }

    private static WorkflowStage Stage(
        string id,
        string name,
        string category,
        IReadOnlyList<string> dependsOn,
        WorkflowStageStatus status,
        string? output = null,
        bool requiresApproval = false)
    {
        return new WorkflowStage(id, name, category, dependsOn, status, requiresApproval, 0, MaximumAttempts, output, null, []);
    }

    private static void RefreshReadiness(EngineeringWorkflowState state)
    {
        var byId = state.Stages.ToDictionary(stage => stage.Id, StringComparer.Ordinal);
        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            if (stage.Status != WorkflowStageStatus.Blocked)
            {
                continue;
            }

            if (stage.DependsOn.Any(dependency => byId[dependency].Status == WorkflowStageStatus.Skipped))
            {
                state.Stages[index] = stage with { Status = WorkflowStageStatus.Skipped };
                continue;
            }

            if (stage.DependsOn.All(dependency => byId[dependency].Status == WorkflowStageStatus.Completed))
            {
                if (stage.Id == "codebase-impact" && state.Analysis.ScenarioType == "Greenfield")
                {
                    state.Stages[index] = stage with
                    {
                        Status = WorkflowStageStatus.Completed,
                        Output = "Not applicable: Greenfield work has no existing code to analyze."
                    };
                    AddEvent(state, "StageNotApplicable", stage.Id, "system", "Codebase impact analysis skipped for Greenfield.");
                    byId[stage.Id] = state.Stages[index];
                    continue;
                }

                var approved = state.Approvals.Any(approval =>
                    approval.PlanVersion == state.PlanVersion && approval.StageId == stage.Id && approval.Approved);
                state.Stages[index] = stage with
                {
                    Status = stage.RequiresApproval && !approved
                        ? WorkflowStageStatus.AwaitingApproval
                        : WorkflowStageStatus.Ready
                };
            }
        }

        if (state.Status is WorkflowRunStatus.SafeStopped or WorkflowRunStatus.Completed)
        {
            return;
        }

        state.Status = state.Stages.Any(stage => stage.Status == WorkflowStageStatus.AwaitingApproval)
            ? WorkflowRunStatus.AwaitingApproval
            : WorkflowRunStatus.Active;
    }

    private static WorkflowStage FindStage(EngineeringWorkflowState state, string stageId)
    {
        return state.Stages.SingleOrDefault(stage => stage.Id == stageId)
            ?? throw new WorkflowValidationException($"Unknown stage '{stageId}'.");
    }

    private static void ReplaceStage(EngineeringWorkflowState state, WorkflowStage updatedStage)
    {
        var index = state.Stages.FindIndex(stage => stage.Id == updatedStage.Id);
        state.Stages[index] = updatedStage;
    }

    private static void AddEvent(EngineeringWorkflowState state, string type, string? stageId, string actor, string detail)
    {
        state.Events.Add(new WorkflowAuditEvent(
            state.Events.Count == 0 ? 1 : state.Events[^1].Sequence + 1,
            DateTime.UtcNow,
            state.PlanVersion,
            type,
            stageId,
            actor,
            detail));
    }

    private static void SafeStop(EngineeringWorkflowState state, string? stageId, string reason)
    {
        state.Status = WorkflowRunStatus.SafeStopped;
        state.CompletedAtUtc = DateTime.UtcNow;
        for (var index = 0; index < state.Stages.Count; index++)
        {
            if (state.Stages[index].Status is WorkflowStageStatus.Blocked or WorkflowStageStatus.Ready or WorkflowStageStatus.AwaitingApproval)
            {
                state.Stages[index] = state.Stages[index] with { Status = WorkflowStageStatus.Skipped };
            }
        }

        AddEvent(state, "SafeStop", stageId, "system", reason);
    }

    private static void PreserveCurrentPlan(EngineeringWorkflowState state)
    {
        state.PreviousPlans.Add(new WorkflowPlanVersion(
            state.PlanVersion,
            state.Requirement,
            state.Analysis,
            state.Stages.ToArray(),
            DateTime.UtcNow));
    }

    private static HashSet<string> FindDescendants(IReadOnlyList<WorkflowStage> stages, string stageId)
    {
        var descendants = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var stage in stages)
            {
                if (!descendants.Contains(stage.Id)
                    && stage.DependsOn.Any(dependency => dependency == stageId || descendants.Contains(dependency)))
                {
                    changed |= descendants.Add(stage.Id);
                }
            }
        }

        return descendants;
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static void EnsureGreenfieldPaths(IReadOnlyList<string>? references)
    {
        const string root = "generated/greenfield/";
        foreach (var reference in references ?? [])
        {
            var normalized = reference.Replace('\\', '/');
            if (!normalized.StartsWith(root, StringComparison.Ordinal)
                || normalized.Length == root.Length
                || normalized.Split('/').Contains(".."))
            {
                throw new WorkflowValidationException($"Greenfield artifacts must be under '{root}<project-slug>'.");
            }
        }
    }

    private static void EnsureReportedChangesMatch(
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        IReadOnlyList<string>? reportedFiles)
    {
        foreach (var path in reportedFiles ?? [])
        {
            var normalized = NormalizeReference(path);
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith('/')
                || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw new WorkflowValidationException($"Changed file path '{path}' must be a normalized workspace-relative path.");
            }
        }

        var reported = (reportedFiles ?? [])
            .Select(NormalizeReference)
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = changedFiles.Select(change => NormalizeReference(change.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!reported.SetEquals(changed))
        {
            var missing = changed.Except(reported, StringComparer.OrdinalIgnoreCase);
            var unexpected = reported.Except(changed, StringComparer.OrdinalIgnoreCase);
            throw new WorkflowValidationException(
                $"Implementation evidence must list exactly the changed workspace files. Missing: [{string.Join(", ", missing)}]. Unchanged or unknown: [{string.Join(", ", unexpected)}].");
        }
    }

    private static string NormalizeReference(string path) => path.Replace('\\', '/');

    private static void EnsureCanReplan(EngineeringWorkflowState state)
    {
        if (state.Status == WorkflowRunStatus.Completed
            || state.Stages.Any(stage => stage.Status == WorkflowStageStatus.Running))
        {
            throw new WorkflowConflictException("Completed or currently running workflows cannot be re-planned.");
        }
    }

    private static IEnumerable<double> CalculateMttrSamples(EngineeringWorkflowState state)
    {
        foreach (var failedEvent in state.Events.Where(item => item.Type == "StageFailed"))
        {
            var recovery = state.Events.FirstOrDefault(item =>
                item.PlanVersion == failedEvent.PlanVersion
                && item.StageId == failedEvent.StageId
                && item.Type == "StageCompleted"
                && item.Sequence > failedEvent.Sequence);
            if (recovery is not null)
            {
                yield return (recovery.AtUtc - failedEvent.AtUtc).TotalSeconds;
            }
        }
    }
}