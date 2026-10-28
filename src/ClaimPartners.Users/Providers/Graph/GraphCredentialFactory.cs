using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using ClaimPartners.Users.Configuration;

namespace ClaimPartners.Users.Providers.Graph;

/// <summary>
/// Credential of the Graph app registration in the external tenant. Never <c>DefaultAzureCredential</c>:
/// a managed identity belongs to the workforce tenant and can't act inside the external tenant.
/// </summary>
internal static class GraphCredentialFactory
{
    public static TokenCredential Create(PartnerUserOptions options, IGraphClientAssertionProvider? clientAssertionProvider = null)
    {
        if (clientAssertionProvider is not null)
        {
            return new ClientAssertionCredential(options.TenantId, options.GraphClientId, AssertionCallback(options, clientAssertionProvider));
        }

        if (!string.IsNullOrWhiteSpace(options.GraphClientSecret))
        {
            return new ClientSecretCredential(options.TenantId, options.GraphClientId, options.GraphClientSecret);
        }

        var certificate = FindCertificate(options.GraphCertificateThumbprint!);
        return new ClientCertificateCredential(options.TenantId, options.GraphClientId, certificate);
    }

    // The assertion targets the endpoint the credential requests tokens from (default authority host).
    internal static Func<CancellationToken, Task<string>> AssertionCallback(PartnerUserOptions options, IGraphClientAssertionProvider clientAssertionProvider)
    {
        var audience = new Uri(AzureAuthorityHosts.AzurePublicCloud, $"{options.TenantId}/oauth2/v2.0/token").ToString();
        return cancellationToken => clientAssertionProvider.GetAssertionAsync(options.GraphClientId, audience, cancellationToken);
    }

    private static X509Certificate2 FindCertificate(string thumbprint)
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (found.Count > 0)
            {
                return found[0];
            }
        }

        throw new InvalidOperationException(
            $"{PartnerUserOptions.SectionName}:{nameof(PartnerUserOptions.GraphCertificateThumbprint)}: no certificate with this thumbprint in the CurrentUser or LocalMachine 'My' store.");
    }
}
