namespace ClaimPartners.Users.Core;

/// <summary>
/// User to create.
/// </summary>
/// <param name="Email">Email address the user will sign in with.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Role">Role value, as defined on the web application (e.g. <c>Agent</c>).</param>
public sealed record CreatePartnerUserRequest(string Email, string DisplayName, string Role);
