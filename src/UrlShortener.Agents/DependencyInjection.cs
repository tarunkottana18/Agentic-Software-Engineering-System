using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using UrlShortener.Agents.Runtime;
using UrlShortener.Application.Interfaces;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kernel>(_ =>
        {
            var builder = Kernel.CreateBuilder();
            var modelId = configuration["SemanticKernel:ModelId"] ?? "gpt-4";
            var apiKey = configuration["SemanticKernel:ApiKey"] ?? string.Empty;
            var endpoint = configuration["SemanticKernel:Endpoint"];
#pragma warning disable SKEXP0010 // Custom OpenAI-compatible endpoint (for example Ollama).
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                && !endpointUri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddOpenAIChatCompletion(modelId, endpointUri, apiKey);
            }
            else
            {
                builder.AddOpenAIChatCompletion(modelId, apiKey);
            }
#pragma warning restore SKEXP0010
            return builder.Build();
        });

        services.AddSingleton<IAgentPromptCatalog, EmbeddedAgentPromptCatalog>();
        services.AddScoped<IAgenticOrchestrator, SemanticKernelOrchestrator>();
        services.AddScoped<IRequirementAnalyzer, SemanticKernelRequirementAnalyzer>();
        services.AddScoped<IWorkflowStageExecutor, SemanticKernelWorkflowStageExecutor>();
        return services;
    }
}
