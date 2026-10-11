using Microsoft.Graph;

namespace ClaimPartners.Users.Providers.Graph;

/// <summary>
/// Holds the package's own <see cref="GraphServiceClient"/>, so it never collides with a client the host registers.
/// Registered as a singleton: the client and its credential cache the Graph token (about one hour).
/// </summary>
internal sealed class PartnerUsersGraphClient(GraphServiceClient client)
{
    public GraphServiceClient Client { get; } = client;
}
