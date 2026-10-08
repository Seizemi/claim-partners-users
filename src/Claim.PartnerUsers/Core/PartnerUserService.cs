using Claim.PartnerUsers.Configuration;
using Claim.PartnerUsers.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Claim.PartnerUsers.Core;

internal sealed partial class PartnerUserService(
    IPartnerUserProvider provider,
    IOptions<PartnerUserOptions> options,
    ILogger<PartnerUserService> logger) : IPartnerUserService
{
    private readonly Dictionary<string, Guid> roles = options.Value.Roles;

    public async Task<PartnerUser> CreateUserAsync(CreatePartnerUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        if (request.Role is null || !roles.TryGetValue(request.Role, out var appRoleId))
        {
            throw new PartnerUserException(PartnerUserErrorCode.UnknownRole, $"Role '{request.Role}' is not configured.");
        }

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();
        var maskedEmail = EmailMask.Mask(email);

        if (await provider.EmailExistsAsync(email, cancellationToken))
        {
            throw new PartnerUserException(PartnerUserErrorCode.UserAlreadyExists, $"A user with the email {maskedEmail} already exists.");
        }

        var userId = await provider.CreateUserAsync(new NewUser(email, displayName, PasswordGenerator.Generate()), cancellationToken);

        try
        {
            await provider.AssignRoleAsync(userId, appRoleId, cancellationToken);
        }
        catch (Exception exception)
        {
            await DeleteOrphanAsync(userId, exception);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new PartnerUserException(PartnerUserErrorCode.GraphError, "The role assignment failed; the created user was removed.", exception);
        }

        LogUserCreated(userId, request.Role, maskedEmail);
        return new PartnerUser(userId, displayName, email, request.Role);
    }

    public async Task<IReadOnlyList<PartnerUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await provider.ListRoleAssignmentsAsync(cancellationToken);
        var roleIdByUser = assignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(group => group.Key, group => group.First().AppRoleId);

        if (roleIdByUser.Count == 0)
        {
            return [];
        }

        var users = await provider.GetUsersAsync(roleIdByUser.Keys, cancellationToken);
        return users
            .Select(user => ToPartnerUser(user, roleIdByUser[user.Id]))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<PartnerUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!IsObjectId(userId))
        {
            return null;
        }

        var user = await provider.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roleId = await provider.GetRoleIdAsync(userId, cancellationToken);
        return ToPartnerUser(user, roleId);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!IsObjectId(userId))
        {
            throw new PartnerUserException(PartnerUserErrorCode.UserNotFound, $"User '{userId}' was not found.");
        }

        await provider.DeleteUserAsync(userId, cancellationToken);
        LogUserDeleted(userId);
    }

    private async Task DeleteOrphanAsync(string userId, Exception assignmentError)
    {
        LogRoleAssignmentFailed(assignmentError, userId);
        try
        {
            // The caller's token may be what cancelled the assignment: the cleanup must still run.
            await provider.DeleteUserAsync(userId, CancellationToken.None);
        }
        catch (Exception deleteError)
        {
            LogOrphanNotDeleted(deleteError, userId);
        }
    }

    private PartnerUser ToPartnerUser(UserRecord user, Guid? roleId) =>
        new(user.Id, user.DisplayName, user.Email, roleId is { } id ? RoleName(id) : null);

    private string? RoleName(Guid appRoleId) =>
        roles.FirstOrDefault(role => role.Value == appRoleId).Key;

    private static bool IsObjectId(string? userId) => Guid.TryParse(userId, out _);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner user {UserId} created with role {Role} ({Email}).")]
    private partial void LogUserCreated(string userId, string role, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner user {UserId} deleted.")]
    private partial void LogUserDeleted(string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Role assignment failed for the new user {UserId}; deleting the user.")]
    private partial void LogRoleAssignmentFailed(Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The user {UserId} was created without a role and could not be deleted. Delete it manually in the Entra admin center.")]
    private partial void LogOrphanNotDeleted(Exception exception, string userId);
}
