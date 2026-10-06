using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Features.Links;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("")]
public sealed class RedirectController(ISender sender) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<IActionResult> RedirectToOriginalUrl(string code, CancellationToken cancellationToken)
    {
        var originalUrl = await sender.Send(new GetOriginalUrlQuery(code), cancellationToken);
        return originalUrl is null ? NotFound() : Redirect(originalUrl);
    }
}