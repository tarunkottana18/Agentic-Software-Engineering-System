using MediatR;
using UrlShortener.Application.Dtos;

namespace UrlShortener.Application.Features.Urls.Queries
{
    public record GetOriginalUrlQuery(string ShortCode) : IRequest<UrlRedirectResponse?>;
}
