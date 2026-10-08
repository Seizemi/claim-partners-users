using Microsoft.Extensions.Options;

namespace Claim.PartnerUsers.Configuration;

internal sealed class PartnerUserOptionsValidator : IValidateOptions<PartnerUserOptions>
{
    public ValidateOptionsResult Validate(string? name, PartnerUserOptions options)
    {
        var failures = new List<string>();

        RequireGuid(options.TenantId, nameof(options.TenantId), failures);
        RequireGuid(options.GraphClientId, nameof(options.GraphClientId), failures);
        RequireGuid(options.AppServicePrincipalId, nameof(options.AppServicePrincipalId), failures);

        if (string.IsNullOrWhiteSpace(options.TenantDomain))
        {
            failures.Add(Message(nameof(options.TenantDomain), "is required (<subdomain>.onmicrosoft.com)."));
        }

        var hasSecret = !string.IsNullOrWhiteSpace(options.GraphClientSecret);
        var hasCertificate = !string.IsNullOrWhiteSpace(options.GraphCertificateThumbprint);
        if (hasSecret == hasCertificate)
        {
            failures.Add($"{PartnerUserOptions.SectionName}: exactly one of {nameof(options.GraphClientSecret)} or {nameof(options.GraphCertificateThumbprint)} must be set.");
        }

        if (options.Roles.Count == 0)
        {
            failures.Add(Message(nameof(options.Roles), "must map at least one role name to its app role ID."));
        }

        foreach (var (role, id) in options.Roles)
        {
            if (id == Guid.Empty)
            {
                failures.Add(Message($"{nameof(options.Roles)}:{role}", "must be a non-empty app role ID."));
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireGuid(string value, string key, List<string> failures)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            failures.Add(Message(key, "is required and must be a GUID."));
        }
    }

    private static string Message(string key, string problem) => $"{PartnerUserOptions.SectionName}:{key} {problem}";
}
