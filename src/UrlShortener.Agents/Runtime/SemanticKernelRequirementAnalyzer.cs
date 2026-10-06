using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Agents.Runtime;

public sealed class SemanticKernelRequirementAnalyzer : IRequirementAnalyzer
{
    // Comparative quality words with no measurable criterion need human clarification.
    private static readonly Regex VagueQualityWords = new(
        @"\b(safer|better|faster|nicer|cleaner|simpler|more (secure|reliable|robust|scalable)|enhance|optimi[sz]e)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Kernel _kernel;
    private readonly string? _apiKey;
    private readonly IAgentPromptCatalog _prompts;

    public SemanticKernelRequirementAnalyzer(
        Kernel kernel,
        IConfiguration configuration,
        IAgentPromptCatalog prompts)
    {
        _kernel = kernel;
        _apiKey = configuration["SemanticKernel:ApiKey"];
        _prompts = prompts;
        _kernel.ImportPluginFromObject(new WorkflowScenarioRoutingPlugin(), "ScenarioRouter");
    }

    public async Task<RequirementAnalysis> AnalyzeAsync(string requirement, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
        {
            return CreateFallback(requirement, "local-fallback-no-model-key");
        }

        try
        {
            var arguments = new KernelArguments(new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
            {
                ["requirement"] = requirement
            };
            var result = await _kernel.InvokePromptAsync(
                _prompts.GetPrompt("requirement-analyst.md"),
                arguments,
                cancellationToken: cancellationToken);
            var json = ExtractJson(result.ToString());
            var analysis = JsonSerializer.Deserialize<RequirementAnalysis>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return IsValid(analysis)
                ? analysis! with { PlanningSource = "semantic-kernel" }
                : CreateFallback(requirement, "local-fallback-invalid-model-output");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateFallback(requirement, "local-fallback-model-error");
        }
    }

    private static bool IsValid(RequirementAnalysis? analysis)
    {
        return analysis is not null
            && !string.IsNullOrWhiteSpace(analysis.NormalizedRequirement)
            && analysis.Tasks is { Count: >= 3 and <= 8 }
            && analysis.Tasks.All(task => !string.IsNullOrWhiteSpace(task.Title) && !string.IsNullOrWhiteSpace(task.Description))
            && analysis.ClarificationQuestions is not null
            && analysis.Assumptions is not null
            && analysis.RiskLevel is "Low" or "Medium" or "High"
            && analysis.ScenarioType is "Greenfield" or "Brownfield" or "Ambiguous";
    }

    private static string ExtractJson(string response)
    {
        var firstBrace = response.IndexOf('{');
        var lastBrace = response.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            throw new JsonException("Model response did not contain a JSON object.");
        }

        return response[firstBrace..(lastBrace + 1)];
    }

    private static RequirementAnalysis CreateFallback(string requirement, string source)
    {
        var ambiguous = requirement.Length < 24
            || VagueQualityWords.IsMatch(requirement)
            || requirement.Contains("make it better", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("fix it", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("improve", StringComparison.OrdinalIgnoreCase);
        var highRisk = requirement.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("production", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("deploy", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("publish", StringComparison.OrdinalIgnoreCase);
        var brownfield = requirement.Contains("existing", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("add", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("fix", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("update", StringComparison.OrdinalIgnoreCase)
            || requirement.Contains("refactor", StringComparison.OrdinalIgnoreCase);
        var scenarioType = ambiguous ? "Ambiguous" : brownfield ? "Brownfield" : "Greenfield";

        return new RequirementAnalysis(
            requirement.Trim(),
            ambiguous ? ["Clarify expected behavior, scope, and acceptance criteria before implementation."] : [],
            [],
            highRisk ? "High" : ambiguous ? "Medium" : "Low",
            [
                new WorkflowTask("Define acceptance criteria", "Confirm observable behavior and edge cases."),
                new WorkflowTask("Analyze impacted components", "Identify API, application, domain, persistence, and documentation changes."),
                new WorkflowTask("Implement and validate", "Prepare a reviewable implementation proposal and run relevant tests."),
                new WorkflowTask("Update documentation and review release readiness", "Record setup, behavior, risks, and validation evidence.")
            ],
            source,
            scenarioType);
    }
}
