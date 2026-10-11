using ClaimPartners.Users.Domain;
using Microsoft.Extensions.Logging;

namespace ClaimPartners.Users.Core;

internal sealed partial class PartnerUserService(
    IPartnerUserProvider provider,
    AppRoleCatalog appRoles,
    ILogger<PartnerUserService> logger) : IPartnerUserService
{

    public async Task<PartnerUser> CreateUserAsync(CreatePartnerUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        // Checked before anything is created, although the role is assigned later by AssignRoleAsync.
        await GetRoleIdAsync(request.Role, cancellationToken);

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();
        var maskedEmail = EmailMask.Mask(email);

        if (await provider.EmailExistsAsync(email, cancellationToken))
        {
            throw new PartnerUserException(PartnerUserErrorCode.UserAlreadyExists, $"A user with the email {maskedEmail} already exists.");
        }

        var userId = await provider.CreateUserAsync(new NewUser(email, displayName, PasswordGenerator.Generate()), cancellationToken);

        LogUserCreated(userId, maskedEmail);
        return new PartnerUser(userId, displayName, email, Role: null);
    }

    public async Task AssignRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!IsObjectId(userId))
        {
            throw new PartnerUserException(PartnerUserErrorCode.UserNotFound, $"User '{userId}' was not found.");
        }

        var appRoleId = await GetRoleIdAsync(role, cancellationToken);
        await provider.AssignRoleAsync(userId, appRoleId, cancellationToken);
        LogRoleAssigned(userId, role);
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

        var roles = await appRoles.GetAsync(cancellationToken);
        var users = await provider.GetUsersAsync(roleIdByUser.Keys, cancellationToken);
        return users
            .Select(user => ToPartnerUser(user, roles.ValueOf(roleIdByUser[user.Id])))
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
        var role = roleId is { } id ? (await appRoles.GetAsync(cancellationToken)).ValueOf(id) : null;
        return ToPartnerUser(user, role);
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

    private async Task<Guid> GetRoleIdAsync(string? role, CancellationToken cancellationToken)
    {
        var roles = await appRoles.GetAsync(cancellationToken);
        return role is not null && roles.TryGetId(role, out var appRoleId)
            ? appRoleId
            : throw new PartnerUserException(PartnerUserErrorCode.UnknownRole, $"Role '{role}' is not an enabled app role of the application.");
    }

    private static PartnerUser ToPartnerUser(UserRecord user, string? role) =>
        new(user.Id, user.DisplayName, user.Email, role);

    private static bool IsObjectId(string? userId) => Guid.TryParse(userId, out _);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner user {UserId} created without role ({Email}).")]
    private partial void LogUserCreated(string userId, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role {Role} assigned to partner user {UserId}.")]
    private partial void LogRoleAssigned(string userId, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner user {UserId} deleted.")]
    private partial void LogUserDeleted(string userId);
}
