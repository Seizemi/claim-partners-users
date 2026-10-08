using Claim.PartnerUsers.Domain;

namespace Claim.PartnerUsers.Core;

/// <summary>
/// Manages the users of the external tenant and their app role on the web application.
/// Every failure is reported as a <see cref="PartnerUserException"/>.
/// </summary>
public interface IPartnerUserService
{
    /// <summary>
    /// Creates a user with an email identity and assigns them a role. A random password is set and discarded:
    /// the user chooses their own through the self-service password reset flow.
    /// </summary>
    /// <exception cref="PartnerUserException">
    /// <see cref="PartnerUserErrorCode.UnknownRole"/>, <see cref="PartnerUserErrorCode.UserAlreadyExists"/>
    /// or <see cref="PartnerUserErrorCode.GraphError"/>. If the role assignment fails, the created user is deleted.
    /// </exception>
    Task<PartnerUser> CreateUserAsync(CreatePartnerUserRequest request, CancellationToken cancellationToken = default);

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
