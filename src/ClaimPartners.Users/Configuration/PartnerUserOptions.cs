namespace ClaimPartners.Users.Configuration;

/// <summary>
/// Settings of the package, bound from the <c>PartnerUsers</c> configuration section.
/// Exactly one of <see cref="GraphClientSecret"/> or <see cref="GraphCertificateThumbprint"/> must be set.
/// </summary>
public sealed class PartnerUserOptions
{
    /// <summary>
    /// Name of the configuration section the options are bound from.
    /// </summary>
    public const string SectionName = "PartnerUsers";

    /// <summary>
    /// ID of the external tenant.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Domain of the external tenant (<c>&lt;subdomain&gt;.onmicrosoft.com</c>), used as the issuer of the users' email identity.
    /// </summary>
    public string TenantDomain { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the app registration calling Microsoft Graph.
    /// </summary>
    public string GraphClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret of the Graph app registration. Allowed in development only.
    /// </summary>
    public string? GraphClientSecret { get; set; }

    /// <summary>
    /// Thumbprint of the Graph app registration's certificate, loaded from the current user's or the local machine's certificate store.
    /// </summary>
    public string? GraphCertificateThumbprint { get; set; }

    /// <summary>
    /// Object ID of the service principal (enterprise application) of the web app the users are assigned to.
    /// </summary>
    /// <remarks>
    /// The app roles of this application are read from it (by value) on first use, so their IDs are not configured.
    /// </remarks>
    public string AppServicePrincipalId { get; set; } = string.Empty;
}
