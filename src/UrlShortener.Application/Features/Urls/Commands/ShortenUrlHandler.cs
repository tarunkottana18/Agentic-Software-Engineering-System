using MediatR;
using UrlShortener.Application.Common;
using UrlShortener.Application.Dtos;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Urls.Commands
{
    public class ShortenUrlHandler : IRequestHandler<ShortenUrlCommand, UrlShortenResponse>
    {
        private readonly IUrlRepository _repository;
        private readonly UrlShortenerSettings _settings;
        private readonly IShortCodeGenerator _shortCodeGenerator;
        private const int MaxCodeGenerationAttempts = 5;

        public ShortenUrlHandler(
            IUrlRepository repository,
            UrlShortenerSettings settings,
            IShortCodeGenerator shortCodeGenerator)
        {
            _repository = repository;
            _settings = settings;
            _shortCodeGenerator = shortCodeGenerator;
        }

        public async Task<UrlShortenResponse> Handle(ShortenUrlCommand request, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(request.OriginalUrl, UriKind.Absolute, out var originalUri)
                || (originalUri.Scheme != Uri.UriSchemeHttp && originalUri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(originalUri.Host))
            {
                throw new InvalidUrlException("The URL must be an absolute HTTP or HTTPS URL.");
            }

            var existing = await _repository.GetByOriginalUrlAsync(request.OriginalUrl);
            if (existing != null)
            {
                return CreateResponse(existing.ShortCode);
            }

            for (var attempt = 0; attempt < MaxCodeGenerationAttempts; attempt++)
            {
                var shortCode = _shortCodeGenerator.Generate();
                var mapping = new UrlMapping
                {
                    Id = Guid.NewGuid(),
                    OriginalUrl = request.OriginalUrl,
                    ShortCode = shortCode,
                    CreatedAt = DateTime.UtcNow
                };

                try
                {
                    await _repository.AddAsync(mapping);
                    return CreateResponse(shortCode);
                }
                catch (ShortCodeConflictException exception)
                {
                    if (attempt == MaxCodeGenerationAttempts - 1)
                    {
                        throw new ShortCodeGenerationException(
                            "Could not allocate a unique short code after several attempts.", exception);
                    }
                }
            }

            throw new ShortCodeGenerationException("Could not allocate a unique short code after several attempts.");
        }

        private UrlShortenResponse CreateResponse(string shortCode)
        {
            var baseUri = new Uri(_settings.BaseShortUrl, UriKind.Absolute);
            var shortUri = new Uri(baseUri, Uri.EscapeDataString(shortCode));
            return new UrlShortenResponse(shortCode, shortUri.ToString());
        }
    }
}
