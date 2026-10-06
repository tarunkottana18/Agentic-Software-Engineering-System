using MediatR;
using UrlShortener.Application.Dtos;

namespace UrlShortener.Application.Features.Urls.Commands
{
    public record ShortenUrlCommand(string OriginalUrl) : IRequest<UrlShortenResponse>;
}
