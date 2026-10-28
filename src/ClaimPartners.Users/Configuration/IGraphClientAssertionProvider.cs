namespace ClaimPartners.Users.Configuration;

/// <summary>
/// Supplies the client assertions the Graph app registration signs in with, instead of a client secret or a
/// certificate from the local store: typically a JWT signed with a certificate the host reads from Azure Key Vault.
/// </summary>
/// <remarks>
/// The package still builds the credential itself, for its own tenant and Graph client ID: the host only signs.
/// </remarks>
public interface IGraphClientAssertionProvider
{
    /// <summary>
    /// Returns a signed JWT client assertion (RFC 7523) for <paramref name="clientId"/>, valid for <paramref name="audience"/>.
    /// Called each time a Graph token is requested (about once an hour, the token being cached).
    /// </summary>
    /// <param name="clientId">The Graph app registration's client ID: the assertion's issuer and subject.</param>
    /// <param name="audience">The token endpoint of the external tenant: the assertion's audience.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<string> GetAssertionAsync(string clientId, string audience, CancellationToken cancellationToken);
}
