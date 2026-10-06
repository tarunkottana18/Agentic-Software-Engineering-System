using MediatR;
using UrlShortener.Application.Interfaces;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Application.Features.Links;

public sealed record CreateLinkCommand(string? OriginalUrl) : IRequest<CreateLinkResult>;

public sealed record CreateLinkResult(bool IsValid, bool IsUnavailable, bool Reused, string? Code);

public sealed class CreateLinkHandler(ILinkRepository repository, IShortCodeGenerator codeGenerator)
    : IRequestHandler<CreateLinkCommand, CreateLinkResult>
{
    private const int MaximumOriginalUrlLength = 2048;
    private const int MaximumCodeAttempts = 5;

    public async Task<CreateLinkResult> Handle(CreateLinkCommand request, CancellationToken cancellationToken)
    {
        if (!IsValidOriginalUrl(request.OriginalUrl))
        {
            return new CreateLinkResult(false, false, false, null);
        }

        var existing = await repository.FindByOriginalUrlAsync(request.OriginalUrl!, cancellationToken);
        if (existing is not null)
        {
            return CreateResult(existing, reused: true);
        }

        for (var attempt = 0; attempt < MaximumCodeAttempts; attempt++)
        {
            var link = new ShortLink(codeGenerator.Generate(), request.OriginalUrl!);
            var status = await repository.TryCreateAsync(link, cancellationToken);

            if (status == LinkCreateStatus.Created)
            {
                return CreateResult(link, reused: false);
            }

            if (status == LinkCreateStatus.OriginalUrlConflict)
            {
                var winner = await repository.FindByOriginalUrlAsync(request.OriginalUrl!, cancellationToken);
                if (winner is not null)
                {
                    return CreateResult(winner, reused: true);
                }

                throw new InvalidOperationException("A conflicting URL mapping was not found.");
            }
        }

        return new CreateLinkResult(true, true, false, null);
    }

    private static CreateLinkResult CreateResult(ShortLink link, bool reused) =>
        new(true, false, reused, link.Code);

    private static bool IsValidOriginalUrl(string? originalUrl)
    {
        if (originalUrl is null || originalUrl.Length > MaximumOriginalUrlLength ||
            originalUrl.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
               !string.IsNullOrEmpty(uri.Host);
    }
}