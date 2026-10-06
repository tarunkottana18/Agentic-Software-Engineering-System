using MediatR;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Links;

public sealed record GetOriginalUrlQuery(string Code) : IRequest<string?>;

public sealed class GetOriginalUrlHandler(ILinkRepository repository)
    : IRequestHandler<GetOriginalUrlQuery, string?>
{
    public Task<string?> Handle(GetOriginalUrlQuery request, CancellationToken cancellationToken) =>
        repository.IncrementClickCountAndGetOriginalUrlAsync(request.Code, cancellationToken);
}