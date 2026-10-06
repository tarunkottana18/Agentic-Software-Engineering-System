using MediatR;
using UrlShortener.Application.Dtos;

namespace UrlShortener.Application.Features.Urls.Queries
{
    public record GetUrlAnalyticsQuery(string ShortCode) : IRequest<UrlAnalyticsResponse?>;
}