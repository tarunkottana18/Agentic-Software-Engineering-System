using MediatR;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Links;

public sealed record GetLinkAnalyticsQuery(string Code) : IRequest<LinkAnalytics?>;

public sealed class GetLinkAnalyticsHandler(ILinkRepository repository)
    : IRequestHandler<GetLinkAnalyticsQuery, LinkAnalytics?>
{
    public Task<LinkAnalytics?> Handle(GetLinkAnalyticsQuery request, CancellationToken cancellationToken) =>
        repository.GetAnalyticsAsync(request.Code, cancellationToken);
}