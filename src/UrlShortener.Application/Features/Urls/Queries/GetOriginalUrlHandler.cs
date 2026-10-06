using MediatR;
using UrlShortener.Application.Dtos;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Urls.Queries
{
    public class GetOriginalUrlHandler : IRequestHandler<GetOriginalUrlQuery, UrlRedirectResponse?>
    {
        private readonly IUrlRepository _repository;

        public GetOriginalUrlHandler(IUrlRepository repository)
        {
            _repository = repository;
        }

        public async Task<UrlRedirectResponse?> Handle(GetOriginalUrlQuery request, CancellationToken cancellationToken)
        {
            var mapping = await _repository.GetByShortCodeAsync(request.ShortCode);
            if (mapping == null) return null;

            await _repository.IncrementClickCountAsync(request.ShortCode);

            return new UrlRedirectResponse(mapping.OriginalUrl);
        }
    }
}
