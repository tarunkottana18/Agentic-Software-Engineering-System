using Microsoft.AspNetCore.Mvc;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("")]
public sealed class ApiInfoController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "URL Shortener API",
            status = "ready",
            endpoints = new[]
            {
                new { method = "POST", path = "/api/v1/links", description = "Create a shortened link." },
                new { method = "GET", path = "/{code}", description = "Redirect to the original URL." },
                new { method = "GET", path = "/api/v1/links/{code}/analytics", description = "Read click analytics." }
            }
        });
    }
}