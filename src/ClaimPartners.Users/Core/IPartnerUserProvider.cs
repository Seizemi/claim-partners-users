namespace ClaimPartners.Users.Core;

/// <summary>
/// Port to the identity provider. Implementations hold no business logic and report failures
/// as <see cref="Domain.PartnerUserException"/>.
/// </summary>
internal interface IPartnerUserProvider
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <returns>The object ID of the created user.</returns>
    Task<string> CreateUserAsync(NewUser user, CancellationToken cancellationToken);

    /// <summary>
    /// The enabled app roles defined on the web application.
    /// </summary>
    Task<IReadOnlyList<AppRoleDefinition>> GetAppRolesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Assigns an app role to a user; an existing assignment of the same role counts as success. The user may have
    /// been created moments ago: the call waits a few seconds for the directory to replicate them before failing.
    /// </summary>
    Task AssignRoleAsync(string userId, Guid appRoleId, CancellationToken cancellationToken);

    /// <summary>
    /// Role assignments of users (not groups or applications) on the web application.
    /// </summary>
    Task<IReadOnlyList<UserRoleAssignment>> ListRoleAssignmentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Users with these IDs; IDs that don't exist are skipped.
    /// </summary>
    Task<IReadOnlyList<UserRecord>> GetUsersAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    Task<UserRecord?> GetUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// ID of the user's app role on the web application, or <c>null</c> without assignment.
    /// </summary>
    Task<Guid?> GetRoleIdAsync(string userId, CancellationToken cancellationToken);

    Task DeleteUserAsync(string userId, CancellationToken cancellationToken);
}

internal sealed record NewUser(string Email, string DisplayName, string Password)
{
    // Keeps the password out of logs and debugger views.
    public override string ToString() => $"NewUser {{ DisplayName = {DisplayName} }}";
}

internal sealed record UserRecord(string Id, string DisplayName, string Email);

internal sealed record UserRoleAssignment(string UserId, Guid AppRoleId);

/// <param name="Value">The role's value: its name in the <c>roles</c> claim, e.g. <c>Agent</c>.</param>
/// <param name="Id">The role's ID, used in role assignments.</param>
internal sealed record AppRoleDefinition(string Value, Guid Id);
