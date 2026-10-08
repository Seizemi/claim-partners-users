namespace Claim.PartnerUsers.Domain;

/// <summary>
/// A user of the external tenant who is assigned to the application.
/// </summary>
/// <param name="Id">Object ID of the user in Entra (the <c>oid</c> claim).</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Email">Email address the user signs in with.</param>
/// <param name="Role">
/// Name of the user's app role (a key of <c>PartnerUsers:Roles</c>), or <c>null</c> when the user
/// has no assignment on the application or one with a role unknown to the configuration.
/// </param>
public sealed record PartnerUser(string Id, string DisplayName, string Email, string? Role);
