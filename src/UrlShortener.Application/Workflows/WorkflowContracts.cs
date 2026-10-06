namespace UrlShortener.Application.Workflows;

public enum WorkflowRunStatus
{
    Active,
    AwaitingApproval,
    Completed,
    SafeStopped
}

public enum WorkflowStageStatus
{
    Blocked,
    Ready,
    Running,
    AwaitingApproval,
    Completed,
    Skipped
}

public sealed record WorkflowStartRequest(string Requirement);

public sealed record WorkflowStageResultRequest(
    bool Succeeded,
    string Output,
    string? FailureReason = null,
    IReadOnlyList<string>? ArtifactReferences = null,
    IReadOnlyList<string>? ChangedFiles = null);

public sealed record WorkflowStageExecutionResult(
    bool Succeeded,
    string Output,
    string? FailureReason = null,
    IReadOnlyList<string>? ArtifactReferences = null);

public sealed record WorkflowApprovalRequest(
    string Approver,
    bool Approved,
    string Rationale,
    string? ApprovedIntent = null);

public sealed record WorkflowRollbackRequest(string Approver, string Rationale);

public sealed record WorkflowPlanRequest(string Requirement);

public sealed record WorkflowStageReplanRequest(string RevisedOutput, string Rationale);

public sealed record WorkflowTask(string Title, string Description);

public sealed record RequirementAnalysis(
    string NormalizedRequirement,
    IReadOnlyList<string> ClarificationQuestions,
    IReadOnlyList<string> Assumptions,
    string RiskLevel,
    IReadOnlyList<WorkflowTask> Tasks,
    string PlanningSource,
    string ScenarioType = "Unclassified");

public sealed record WorkflowStage(
    string Id,
    string Name,
    string Category,
    IReadOnlyList<string> DependsOn,
    WorkflowStageStatus Status,
    bool RequiresApproval,
    int Attempts,
    int MaxAttempts,
    string? Output,
    string? FailureReason,
    IReadOnlyList<string> ArtifactReferences);

public sealed record WorkflowAuditEvent(
    long Sequence,
    DateTime AtUtc,
    int PlanVersion,
    string Type,
    string? StageId,
    string Actor,
    string Detail);

public sealed record WorkflowApproval(
    string StageId,
    string Approver,
    bool Approved,
    string Rationale,
    int PlanVersion,
    DateTime AtUtc);

public sealed record WorkflowPlanVersion(
    int PlanVersion,
    string Requirement,
    RequirementAnalysis Analysis,
    IReadOnlyList<WorkflowStage> Stages,
    DateTime CreatedAtUtc);

public sealed record WorkspaceFileSnapshot(string Path, string Sha256);

public sealed record WorkspaceFileChange(string Path, string? BaselineSha256, string? CurrentSha256);

public sealed class EngineeringWorkflowState
{
    public int SchemaVersion { get; set; } = 1;
    public int PlanVersion { get; set; } = 1;
    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Active;
    public string Requirement { get; set; } = string.Empty;
    public RequirementAnalysis Analysis { get; set; } = new(
        string.Empty, Array.Empty<string>(), Array.Empty<string>(), "Unknown", Array.Empty<WorkflowTask>(), "unknown");
    public List<WorkflowStage> Stages { get; set; } = [];
    public List<WorkflowApproval> Approvals { get; set; } = [];
    public List<WorkflowAuditEvent> Events { get; set; } = [];
    public List<WorkflowPlanVersion> PreviousPlans { get; set; } = [];
    public List<WorkspaceFileSnapshot> ImplementationBaseline { get; set; } = [];
    public List<WorkspaceFileChange> ImplementationChanges { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed record EngineeringWorkflowView(
    Guid Id,
    long Revision,
    EngineeringWorkflowState Workflow);

public sealed record WorkflowMetrics(
    int TotalWorkflows,
    int TerminalWorkflows,
    double SuccessRate,
    int RetryCount,
    int RollbackCount,
    double AverageMttrSeconds,
    double AverageEndToEndLatencySeconds);

public interface IRequirementAnalyzer
{
    Task<RequirementAnalysis> AnalyzeAsync(string requirement, CancellationToken cancellationToken);
}

public interface IWorkflowStageExecutor
{
    Task<WorkflowStageExecutionResult> ExecuteAsync(
        EngineeringWorkflowState workflow,
        WorkflowStage stage,
        CancellationToken cancellationToken);
}

public interface IWorkflowArtifactStore
{
    Task<string> WriteStageArtifactAsync(
        Guid workflowId,
        int planVersion,
        WorkflowStage stage,
        string output,
        CancellationToken cancellationToken);

    Task DeletePlanArtifactsAsync(
        Guid workflowId,
        int planVersion,
        CancellationToken cancellationToken);
}

public interface ITrustedValidationExecutor
{
    bool IsEnabled { get; }

    Task<WorkflowStageExecutionResult> ExecuteAsync(CancellationToken cancellationToken);
}

public interface IEngineeringWorkflowService
{
    Task<EngineeringWorkflowView> CreateAsync(WorkflowStartRequest request, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EngineeringWorkflowView>> GetAllAsync(CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> StartStageAsync(Guid id, string stageId, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> ExecuteReadyStagesAsync(Guid id, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> SubmitStageResultAsync(Guid id, string stageId, WorkflowStageResultRequest request, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> ApproveStageAsync(Guid id, string stageId, WorkflowApprovalRequest request, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> ReplanAsync(Guid id, WorkflowPlanRequest request, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> ReplanFromStageAsync(Guid id, string stageId, WorkflowStageReplanRequest request, CancellationToken cancellationToken);
    Task<EngineeringWorkflowView> RollbackPlanAsync(Guid id, WorkflowRollbackRequest request, CancellationToken cancellationToken);
    Task<WorkflowMetrics> GetMetricsAsync(CancellationToken cancellationToken);
}

public interface IWorkspaceChangeTracker
{
    Task<IReadOnlyList<WorkspaceFileSnapshot>> CaptureAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceFileChange>> FindChangesAsync(
        IReadOnlyList<WorkspaceFileSnapshot> baseline,
        CancellationToken cancellationToken);
}