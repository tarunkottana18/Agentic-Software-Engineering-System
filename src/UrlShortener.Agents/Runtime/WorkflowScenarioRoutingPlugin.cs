using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace UrlShortener.Agents.Runtime;

public sealed class WorkflowScenarioRoutingPlugin
{
    [KernelFunction, Description("Choose this for a requirement that creates a new product or capability without changing existing behavior.")]
    public string SelectGreenfield(
        [Description("The original requirement text")] string requirement,
        [Description("Brief reason this is a new capability")] string rationale)
    {
        return SerializeSelection("Greenfield", requirement, rationale);
    }

    [KernelFunction, Description("Choose this when a requirement changes, fixes, or extends existing code, APIs, data, or behavior.")]
    public string SelectBrownfield(
        [Description("The original requirement text")] string requirement,
        [Description("Brief reason existing code or behavior is affected")] string rationale)
    {
        return SerializeSelection("Brownfield", requirement, rationale);
    }

    [KernelFunction, Description("Choose this when the requirement is underspecified, conflicting, or requires a human clarification before planning implementation.")]
    public string RequestClarification(
        [Description("The original requirement text")] string requirement,
        [Description("One or more concise questions needed to clarify intent")] string questions)
    {
        return JsonSerializer.Serialize(new
        {
            scenarioType = "Ambiguous",
            requirement,
            rationale = "The requirement needs clarification before downstream planning.",
            clarificationQuestions = questions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        });
    }

    private static string SerializeSelection(string scenarioType, string requirement, string rationale)
    {
        return JsonSerializer.Serialize(new { scenarioType, requirement, rationale, clarificationQuestions = Array.Empty<string>() });
    }
}