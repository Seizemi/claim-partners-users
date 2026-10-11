using ClaimPartners.Users.Domain;

namespace ClaimPartners.Users.Core;

/// <summary>
/// Manages the users of the external tenant and their app role on the web application.
/// Every failure is reported as a <see cref="PartnerUserException"/>.
/// </summary>
public interface IPartnerUserService
{
    /// <summary>
    /// Creates a user with an email identity, <b>without</b> a role: the returned user's role is <c>null</c>.
    /// Assign the role afterwards with <see cref="AssignRoleAsync"/>, retrying while it fails: Entra may take from
    /// seconds to over a minute before a new user can be assigned. The requested role is checked before anything is
    /// created. A random password is set and discarded: the user chooses their own through the self-service
    /// password reset flow.
    /// </summary>
    /// <exception cref="PartnerUserException">
    /// <see cref="PartnerUserErrorCode.UnknownRole"/>, <see cref="PartnerUserErrorCode.UserAlreadyExists"/>
    /// or <see cref="PartnerUserErrorCode.GraphError"/>.
    /// </exception>
    Task<PartnerUser> CreateUserAsync(CreatePartnerUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an app role to a user. Idempotent: an existing assignment of the same role counts as success.
    /// Waits a few seconds for a user created moments ago to be replicated, then fails with
    /// <see cref="PartnerUserErrorCode.GraphError"/>; the caller retries later.
    /// </summary>
    /// <exception cref="PartnerUserException">
    /// <see cref="PartnerUserErrorCode.UnknownRole"/>, <see cref="PartnerUserErrorCode.UserNotFound"/> (the ID is not
    /// an object ID) or <see cref="PartnerUserErrorCode.GraphError"/>.
    /// </exception>
    Task AssignRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the users assigned to the web application.
    /// </summary>
    /// <exception cref="PartnerUserException"><see cref="PartnerUserErrorCode.GraphError"/>.</exception>
    Task<IReadOnlyList<PartnerUser>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by object ID, or <c>null</c> if they don't exist.
    /// </summary>
    /// <exception cref="PartnerUserException"><see cref="PartnerUserErrorCode.GraphError"/>.</exception>
    Task<PartnerUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user (soft delete in Entra, recoverable for 30 days).
    /// </summary>
    /// <exception cref="PartnerUserException">
    /// <see cref="PartnerUserErrorCode.UserNotFound"/> or <see cref="PartnerUserErrorCode.GraphError"/>.
    /// </exception>
    Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}
