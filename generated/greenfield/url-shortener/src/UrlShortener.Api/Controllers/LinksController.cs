using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Features.Links;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("api/v1/links")]
public sealed class LinksController(ISender sender, IConfiguration configuration) : ControllerBase
{
    private readonly string _publicBaseUrl = configuration["UrlShortener:PublicBaseUrl"]!.TrimEnd('/');

    [HttpPost]
    public async Task<IActionResult> Create(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateLinkCommand(request.OriginalUrl), cancellationToken);
        if (!result.IsValid)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid original URL.");
        }

        if (result.IsUnavailable)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Unable to allocate a short code.");
        }

        var shortUrl = $"{_publicBaseUrl}/{result.Code}";
        var body = new ShortLinkResponse(result.Code!, shortUrl);
        Response.Headers.Location = shortUrl;
        return result.Reused ? Ok(body) : Created(shortUrl, body);
    }

    [HttpGet("{code}/analytics")]
    public async Task<IActionResult> GetAnalytics(string code, CancellationToken cancellationToken)
    {
        var analytics = await sender.Send(new GetLinkAnalyticsQuery(code), cancellationToken);
        return analytics is null
            ? NotFound()
            : Ok(new LinkAnalyticsResponse(analytics.Code, analytics.ClickCount));
    }
}

public sealed record CreateLinkRequest(string? OriginalUrl);
public sealed record ShortLinkResponse(string Code, string ShortUrl);
public sealed record LinkAnalyticsResponse(string Code, int ClickCount);