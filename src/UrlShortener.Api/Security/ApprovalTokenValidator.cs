using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.Api.Security;

public interface IApprovalTokenValidator
{
    bool IsConfigured { get; }
    bool IsValid(string candidate);
    bool IsRoleAuthorized(string? role);
}

public sealed class ApprovalTokenValidator : IApprovalTokenValidator
{
    private readonly string? _token;
    private readonly string? _requiredRole;

    public ApprovalTokenValidator(IConfiguration configuration)
    {
        _token = configuration["WorkflowGovernance:ApprovalToken"];
        _requiredRole = configuration["WorkflowGovernance:RequiredRole"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

    public bool IsValid(string candidate)
    {
        if (!IsConfigured || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_token!),
            Encoding.UTF8.GetBytes(candidate));
    }

    public bool IsRoleAuthorized(string? role)
    {
        if (string.IsNullOrWhiteSpace(_requiredRole))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(role)
            && string.Equals(_requiredRole.Trim(), role.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}