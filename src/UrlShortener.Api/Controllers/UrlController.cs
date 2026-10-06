using Microsoft.AspNetCore.Mvc;
using MediatR;
using UrlShortener.Application.Common;
using UrlShortener.Application.Dtos;
using UrlShortener.Application.Features.Urls.Commands;
using UrlShortener.Application.Features.Urls.Queries;
using UrlShortener.Application.Interfaces;

namespace UrlShortener.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UrlController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IAgenticOrchestrator _orchestrator;

        public UrlController(IMediator mediator, IAgenticOrchestrator orchestrator)
        {
            _mediator = mediator;
            _orchestrator = orchestrator;
        }

        [HttpPost("shorten")]
        public async Task<IActionResult> Shorten([FromBody] UrlShortenRequest request)
        {
            var command = new ShortenUrlCommand(request.OriginalUrl);
            try
            {
                var result = await _mediator.Send(command);
                return Ok(result);
            }
            catch (InvalidUrlException exception)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid URL",
                    Detail = exception.Message
                });
            }
            catch (ShortCodeGenerationException exception)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Short link temporarily unavailable",
                    Detail = exception.Message
                });
            }
        }

        [HttpGet("redirect/{shortCode}")]
        [HttpGet("~/{shortCode}")]
        public async Task<IActionResult> RedirectByShortCode(string shortCode)
        {
            var query = new GetOriginalUrlQuery(shortCode);
            var result = await _mediator.Send(query);

            if (result == null)
            {
                return NotFound("URL not found.");
            }

            return Redirect(result.OriginalUrl);
        }

        [HttpGet("{shortCode}/analytics")]
        public async Task<IActionResult> GetAnalytics(string shortCode)
        {
            var result = await _mediator.Send(new GetUrlAnalyticsQuery(shortCode));
            return result is null ? NotFound() : Ok(result);
        }

        [HttpPost("agent")]
        public async Task<IActionResult> AskAgent([FromBody] string requirement)
        {
            var result = await _orchestrator.ProcessRequirementAsync(requirement);
            return Ok(new { Response = result });
        }
    }
}
