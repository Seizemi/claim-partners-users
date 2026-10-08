namespace Claim.PartnerUsers.Core;

/// <summary>
/// User to create.
/// </summary>
/// <param name="Email">Email address the user will sign in with.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Role">Role name, a key of <c>PartnerUsers:Roles</c>.</param>
public sealed record CreatePartnerUserRequest(string Email, string DisplayName, string Role);
