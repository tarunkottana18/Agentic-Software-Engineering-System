using MediatR;
using UrlShortener.Application.Dtos;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Urls.Queries
{
    public sealed class GetUrlAnalyticsHandler : IRequestHandler<GetUrlAnalyticsQuery, UrlAnalyticsResponse?>
    {
        private readonly IUrlRepository _repository;

        public GetUrlAnalyticsHandler(IUrlRepository repository)
        {
            _repository = repository;
        }

        public async Task<UrlAnalyticsResponse?> Handle(GetUrlAnalyticsQuery request, CancellationToken cancellationToken)
        {
            var mapping = await _repository.GetByShortCodeAsync(request.ShortCode);
            return mapping is null
                ? null
                : new UrlAnalyticsResponse(mapping.ShortCode, mapping.OriginalUrl, mapping.CreatedAt, mapping.ClickCount);
        }
    }
}