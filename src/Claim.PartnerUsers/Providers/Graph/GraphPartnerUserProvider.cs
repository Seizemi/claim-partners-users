using System.Net;
using Claim.PartnerUsers.Configuration;
using Claim.PartnerUsers.Core;
using Claim.PartnerUsers.Domain;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Options;

namespace Claim.PartnerUsers.Providers.Graph;

internal sealed class GraphPartnerUserProvider(PartnerUsersGraphClient graph, IOptions<PartnerUserOptions> options) : IPartnerUserProvider
{
    // Graph accepts at most 15 values in an "in" filter.
    internal const int MaxIdsPerFilter = 15;
    private const string EmailSignInType = "emailAddress";
    private static readonly string[] UserFields = ["id", "displayName", "mail", "identities"];

    private readonly PartnerUserOptions settings = options.Value;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            var response = await graph.Client.Users.GetAsync(request =>
            {
                request.QueryParameters.Filter =
                    $"identities/any(i:i/issuer eq '{Escape(settings.TenantDomain)}' and i/issuerAssignedId eq '{Escape(email)}')";
                request.QueryParameters.Select = ["id"];
                request.QueryParameters.Top = 1;
            }, cancellationToken);
            return response?.Value is { Count: > 0 };
        });

    public Task<string> CreateUserAsync(NewUser user, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            var body = new User
            {
                AccountEnabled = true,
                DisplayName = user.DisplayName,
                Mail = user.Email,
                Identities =
                [
                    new ObjectIdentity { SignInType = EmailSignInType, Issuer = settings.TenantDomain, IssuerAssignedId = user.Email },
                ],
                PasswordProfile = new PasswordProfile { Password = user.Password, ForceChangePasswordNextSignIn = false },
                PasswordPolicies = "DisablePasswordExpiration",
            };

            try
            {
                var created = await graph.Client.Users.PostAsync(body, cancellationToken: cancellationToken);
                return created?.Id ?? throw new PartnerUserException(PartnerUserErrorCode.GraphError, "Graph returned no ID for the created user.");
            }
            catch (ODataError error) when (IsDuplicateIdentity(error))
            {
                // A user created between the uniqueness check and this call.
                throw new PartnerUserException(PartnerUserErrorCode.UserAlreadyExists, "A user with this email already exists.", error);
            }
        });

    public Task AssignRoleAsync(string userId, Guid appRoleId, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            var assignment = new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(userId),
                ResourceId = Guid.Parse(settings.AppServicePrincipalId),
                AppRoleId = appRoleId,
            };
            await graph.Client.ServicePrincipals[settings.AppServicePrincipalId].AppRoleAssignedTo
                .PostAsync(assignment, cancellationToken: cancellationToken);
            return true;
        });

    public Task<IReadOnlyList<UserRoleAssignment>> ListRoleAssignmentsAsync(CancellationToken cancellationToken) =>
        Call<IReadOnlyList<UserRoleAssignment>>(async () =>
        {
            var assignments = new List<UserRoleAssignment>();
            var builder = graph.Client.ServicePrincipals[settings.AppServicePrincipalId].AppRoleAssignedTo;
            var page = await builder.GetAsync(cancellationToken: cancellationToken);
            while (page is not null)
            {
                assignments.AddRange(
                    from assignment in page.Value ?? []
                    where assignment.PrincipalType == "User" && assignment.PrincipalId is not null && assignment.AppRoleId is not null
                    select new UserRoleAssignment(assignment.PrincipalId!.Value.ToString(), assignment.AppRoleId!.Value));

                page = page.OdataNextLink is { } next
                    ? await builder.WithUrl(next).GetAsync(cancellationToken: cancellationToken)
                    : null;
            }

            return assignments;
        });

    public Task<IReadOnlyList<UserRecord>> GetUsersAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<UserRecord>>(async () =>
        {
            var users = new List<UserRecord>();
            foreach (var chunk in userIds.Chunk(MaxIdsPerFilter))
            {
                var response = await graph.Client.Users.GetAsync(request =>
                {
                    request.QueryParameters.Filter = $"id in ({string.Join(",", chunk.Select(id => $"'{Escape(id)}'"))})";
                    request.QueryParameters.Select = UserFields;
                }, cancellationToken);
                users.AddRange((response?.Value ?? []).Select(ToRecord));
            }

            return users;
        });

    public Task<UserRecord?> GetUserAsync(string userId, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            try
            {
                var user = await graph.Client.Users[userId].GetAsync(
                    request => request.QueryParameters.Select = UserFields, cancellationToken);
                return user is null ? null : ToRecord(user);
            }
            catch (ODataError error) when (error.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    public Task<Guid?> GetRoleIdAsync(string userId, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            var resourceId = Guid.Parse(settings.AppServicePrincipalId);
            var response = await graph.Client.Users[userId].AppRoleAssignments.GetAsync(cancellationToken: cancellationToken);
            return (response?.Value ?? []).FirstOrDefault(assignment => assignment.ResourceId == resourceId)?.AppRoleId;
        });

    public Task DeleteUserAsync(string userId, CancellationToken cancellationToken) =>
        Call(async () =>
        {
            try
            {
                await graph.Client.Users[userId].DeleteAsync(cancellationToken: cancellationToken);
                return true;
            }
            catch (ODataError error) when (error.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new PartnerUserException(PartnerUserErrorCode.UserNotFound, $"User '{userId}' was not found.", error);
            }
        });

    private UserRecord ToRecord(User user)
    {
        var email = user.Identities?
            .FirstOrDefault(identity => identity.SignInType == EmailSignInType)?.IssuerAssignedId
            ?? user.Mail
            ?? string.Empty;
        return new UserRecord(user.Id!, user.DisplayName ?? string.Empty, email);
    }

    private static bool IsDuplicateIdentity(ODataError error) =>
        error.ResponseStatusCode == (int)HttpStatusCode.BadRequest
        && error.Error?.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;

    // OData string literals escape a single quote by doubling it.
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Runs a Graph call and reports any failure other than cancellation as a <see cref="PartnerUserException"/>.
    /// </summary>
    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not PartnerUserException and not OperationCanceledException)
        {
            var status = exception is ODataError error ? $" (HTTP {error.ResponseStatusCode}, {error.Error?.Code})" : string.Empty;
            throw new PartnerUserException(PartnerUserErrorCode.GraphError, $"Microsoft Graph call failed{status}.", exception);
        }
    }
}
