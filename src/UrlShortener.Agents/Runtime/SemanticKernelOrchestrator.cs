using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UrlShortener.Application.Interfaces;
using UrlShortener.Application.Features.Urls.Commands;
using UrlShortener.Application.Features.Urls.Queries;
using MediatR;
using System.ComponentModel;
using System.Threading.Tasks;

namespace UrlShortener.Agents.Runtime
{
    public class SemanticKernelOrchestrator : IAgenticOrchestrator
    {
        private readonly Kernel _kernel;
        private readonly IMediator _mediator;
        private readonly UrlShortener.Application.Common.IPromptSanitizer _sanitizer;

        public SemanticKernelOrchestrator(
            Kernel kernel,
            IMediator mediator,
            UrlShortener.Application.Common.IPromptSanitizer sanitizer)
        {
            _kernel = kernel;
            _mediator = mediator;
            _sanitizer = sanitizer;
            _kernel.ImportPluginFromObject(new UrlShortenerPlugin(_mediator), "UrlPlugin");
        }

        public async Task<string> ProcessRequirementAsync(string requirement)
        {
            // SANITIZATION STEP: Ensure the user hasn't tried to hijack the prompt
            string sanitizedRequirement = _sanitizer.Sanitize(requirement);
            if (sanitizedRequirement.StartsWith("Invalid request"))
            {
                return sanitizedRequirement;
            }

            // The orchestrator uses a Planner or Function Calling to map 
            // Natural Language -> Mediator Commands/Queries
            var arguments = new KernelArguments(new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
            {
                ["input"] = sanitizedRequirement
            };

            var result = await _kernel.InvokePromptAsync(
                "Analyze this requirement and use the available URL tools when needed: {{$input}}",
                arguments);

            return result.ToString();
        }
    }

    // This is the "Bridge" between AI and our SOLID code
    public class UrlShortenerPlugin
    {
        private readonly IMediator _mediator;

        public UrlShortenerPlugin(IMediator mediator)
        {
            _mediator = mediator;
        }

        [KernelFunction, Description("Shortens a long URL to a short code")]
        public async Task<string> ShortenUrl([Description("The original long URL")] string url)
        {
            var response = await _mediator.Send(new ShortenUrlCommand(url));
            return $"Success! Shortened to: {response.ShortCode}. Link: {response.ShortUrl}";
        }

        [KernelFunction, Description("Gets the original URL from a short code")]
        public async Task<string> GetOriginalUrl([Description("The short code")] string code)
        {
            var response = await _mediator.Send(new GetOriginalUrlQuery(code));
            return response != null ? $"Original URL is: {response.OriginalUrl}" : "URL not found.";
        }
    }
}
