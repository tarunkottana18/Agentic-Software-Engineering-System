using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Workflows;
using UrlShortener.Api.Security;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class EngineeringWorkflowsController : ControllerBase
{
    private readonly IEngineeringWorkflowService _workflows;
    private readonly IApprovalTokenValidator _approvalTokenValidator;

    public EngineeringWorkflowsController(
        IEngineeringWorkflowService workflows,
        IApprovalTokenValidator approvalTokenValidator)
    {
        _workflows = workflows;
        _approvalTokenValidator = approvalTokenValidator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(WorkflowStartRequest request, CancellationToken cancellationToken)
    {
        var workflow = await _workflows.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = workflow.Id }, workflow);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EngineeringWorkflowView>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _workflows.GetAllAsync(cancellationToken));
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<WorkflowMetrics>> GetMetrics(CancellationToken cancellationToken)
    {
        return Ok(await _workflows.GetMetricsAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var workflow = await _workflows.GetAsync(id, cancellationToken);
        return workflow is null ? NotFound() : Ok(workflow);
    }

    [HttpPost("{id:guid}/stages/{stageId}/start")]
    public async Task<IActionResult> StartStage(Guid id, string stageId, CancellationToken cancellationToken)
    {
        return Ok(await _workflows.StartStageAsync(id, stageId, cancellationToken));
    }

    [HttpPost("{id:guid}/execute-ready")]
    public async Task<IActionResult> ExecuteReadyStages(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _workflows.ExecuteReadyStagesAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/stages/{stageId}/result")]
    public async Task<IActionResult> SubmitStageResult(
        Guid id,
        string stageId,
        WorkflowStageResultRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _workflows.SubmitStageResultAsync(id, stageId, request, cancellationToken));
    }

    [HttpPost("{id:guid}/stages/{stageId}/approval")]
    public async Task<IActionResult> ApproveStage(
        Guid id,
        string stageId,
        WorkflowApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = CheckApprovalToken();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        return Ok(await _workflows.ApproveStageAsync(id, stageId, request, cancellationToken));
    }

    [HttpPost("{id:guid}/replan")]
    public async Task<IActionResult> Replan(Guid id, WorkflowPlanRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _workflows.ReplanAsync(id, request, cancellationToken));
    }

    [HttpPost("{id:guid}/stages/{stageId}/replan")]
    public async Task<IActionResult> ReplanFromStage(
        Guid id,
        string stageId,
        WorkflowStageReplanRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _workflows.ReplanFromStageAsync(id, stageId, request, cancellationToken));
    }

    [HttpPost("{id:guid}/rollback")]
    public async Task<IActionResult> RollbackPlan(Guid id, WorkflowRollbackRequest request, CancellationToken cancellationToken)
    {
        var authorizationFailure = CheckApprovalToken();
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        return Ok(await _workflows.RollbackPlanAsync(id, request, cancellationToken));
    }

    private IActionResult? CheckApprovalToken()
    {
        if (!_approvalTokenValidator.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Approval is not configured",
                Detail = "Configure WorkflowGovernance:ApprovalToken before enabling approval actions."
            });
        }

        if (!_approvalTokenValidator.IsValid(Request.Headers["X-Approval-Token"].ToString()))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Approval authorization required",
                Detail = "Provide a valid X-Approval-Token header."
            });
        }

        var roleHeader = Request.Headers["X-Approval-Role"].ToString();
        if (!_approvalTokenValidator.IsRoleAuthorized(roleHeader))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Approval role unauthorized",
                Detail = "The provided X-Approval-Role is not authorized for approval actions."
            });
        }

        return null;
    }
}