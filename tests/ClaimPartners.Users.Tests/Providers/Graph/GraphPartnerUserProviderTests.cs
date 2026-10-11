using System.Net;
using System.Text.Json;
using ClaimPartners.Users.Core;
using ClaimPartners.Users.Domain;
using ClaimPartners.Users.Providers.Graph;
using ClaimPartners.Users.Tests.Shared;
using Microsoft.Extensions.Options;

namespace ClaimPartners.Users.Tests.Providers.Graph;

public sealed class GraphPartnerUserProviderTests
{
    private const string UserId = "0b5e8c1d-1111-4f00-9a00-000000000001";
    private readonly StubGraphHandler graph = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // No waiting between replication retries: the tests only count the attempts.
    private GraphPartnerUserProvider CreateSut() =>
        new(graph.CreateClient(), Options.Create(TestOptions.Create()), [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);

    // ---- EmailExistsAsync ----

    [Theory]
    [InlineData("""{ "value": [ { "id": "x" } ] }""", true)]
    [InlineData("""{ "value": [] }""", false)]
    public async Task EmailExistsAsync_SearchesTheEmailIdentityOfTheTenant(string response, bool expected)
    {
        graph.Returns(HttpStatusCode.OK, response);

        var exists = await CreateSut().EmailExistsAsync("o'neil@example.com", Token);

        Assert.Equal(expected, exists);
        var request = Assert.Single(graph.Requests);
        Assert.Equal("/v1.0/users", request.Path);
        Assert.Contains(
            $"identities/any(i:i/issuer eq '{TestOptions.TenantDomain}' and i/issuerAssignedId eq 'o''neil@example.com')",
            request.Query);
    }

    // ---- CreateUserAsync ----

    [Fact]
    public async Task CreateUserAsync_PostsAnEnabledUserWithEmailIdentityAndReturnsItsId()
    {
        graph.Returns(HttpStatusCode.Created, $$"""{ "id": "{{UserId}}" }""");

        var id = await CreateSut().CreateUserAsync(new NewUser("jane@example.com", "Jane", "P@ssw0rd-generated"), Token);

        Assert.Equal(UserId, id);
        var request = Assert.Single(graph.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1.0/users", request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.True(root.GetProperty("accountEnabled").GetBoolean());
        Assert.Equal("Jane", root.GetProperty("displayName").GetString());
        var identity = Assert.Single(root.GetProperty("identities").EnumerateArray());
        Assert.Equal("emailAddress", identity.GetProperty("signInType").GetString());
        Assert.Equal(TestOptions.TenantDomain, identity.GetProperty("issuer").GetString());
        Assert.Equal("jane@example.com", identity.GetProperty("issuerAssignedId").GetString());
        Assert.False(root.GetProperty("passwordProfile").GetProperty("forceChangePasswordNextSignIn").GetBoolean());
    }

    [Fact]
    public async Task CreateUserAsync_IdentityAlreadyExists_ThrowsUserAlreadyExists()
    {
        graph.ReturnsError(HttpStatusCode.BadRequest, "Request_BadRequest",
            "Another object with the same value for property identities already exists.");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(
            () => CreateSut().CreateUserAsync(new NewUser("jane@example.com", "Jane", "secret"), Token));

        Assert.Equal(PartnerUserErrorCode.UserAlreadyExists, exception.Code);
    }

    // ---- AssignRoleAsync ----

    [Fact]
    public async Task AssignRoleAsync_PostsAssignmentOnTheWebAppServicePrincipal()
    {
        graph.Returns(HttpStatusCode.Created, "{}");

        await CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token);

        var request = Assert.Single(graph.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/v1.0/servicePrincipals/{TestOptions.AppServicePrincipalId}/appRoleAssignedTo", request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(UserId, body.RootElement.GetProperty("principalId").GetString());
        Assert.Equal(TestOptions.AppServicePrincipalId, body.RootElement.GetProperty("resourceId").GetString());
        Assert.Equal(TestOptions.AgentRoleId.ToString(), body.RootElement.GetProperty("appRoleId").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Request_ResourceNotFound", "Resource 'x' does not exist or one of its queried reference-property objects are not present.")]
    [InlineData(HttpStatusCode.BadRequest, "Request_BadRequest", "Resource 'x' does not exist or one of its queried reference-property objects are not present.")]
    [InlineData(HttpStatusCode.BadRequest, "Request_BadRequest", "Not a valid reference update.")]
    public async Task AssignRoleAsync_NewUserNotReplicatedYet_RetriesUntilItSucceeds(HttpStatusCode status, string code, string message)
    {
        graph.ReturnsError(status, code, message).ReturnsError(status, code, message).Returns(HttpStatusCode.Created, "{}");

        await CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token);

        Assert.Equal(3, graph.Requests.Count);
        Assert.All(graph.Requests, request => Assert.Equal(HttpMethod.Post, request.Method));
    }

    [Fact]
    public async Task AssignRoleAsync_StillNotFoundAfterAllRetries_ThrowsGraphError()
    {
        for (var i = 0; i < 4; i++)
        {
            graph.ReturnsError(HttpStatusCode.NotFound, "Request_ResourceNotFound", "Resource 'x' does not exist.");
        }

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        Assert.Contains("HTTP 404", exception.Message);
        Assert.Equal(4, graph.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.")]
    [InlineData(HttpStatusCode.BadRequest, "Request_BadRequest", "Permission being assigned was not found on application.")]
    public async Task AssignRoleAsync_OtherError_FailsWithoutRetrying(HttpStatusCode status, string code, string message)
    {
        graph.ReturnsError(status, code, message);

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task AssignRoleAsync_RoleAlreadyAssigned_Succeeds()
    {
        graph.ReturnsError(HttpStatusCode.BadRequest, "Request_BadRequest", "Permission being assigned already exists on the object.");

        await CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token);

        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task AssignRoleAsync_NotReplicatedThenAlreadyAssigned_Succeeds()
    {
        // The first attempt reached a server that assigned the role but its response was lost.
        graph.ReturnsError(HttpStatusCode.BadRequest, "Request_BadRequest", "Not a valid reference update.")
            .ReturnsError(HttpStatusCode.BadRequest, "Request_BadRequest", "Permission being assigned already exists on the object.");

        await CreateSut().AssignRoleAsync(UserId, TestOptions.AgentRoleId, Token);

        Assert.Equal(2, graph.Requests.Count);
    }

    [Fact]
    public void DefaultReplicationDelays_WaitAFewSecondsOnly()
    {
        // Longer waits belong to the caller (background retry): a request must not hang for a minute.
        var total = GraphPartnerUserProvider.DefaultReplicationDelays.Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay);

        Assert.True(total <= TimeSpan.FromSeconds(10), $"Total wait is {total}.");
    }

    [Fact]
    public async Task DeleteUserAsync_NotFound_DoesNotRetry()
    {
        graph.ReturnsError(HttpStatusCode.NotFound, "Request_ResourceNotFound", "Resource 'x' does not exist.");

        await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().DeleteUserAsync(UserId, Token));

        Assert.Single(graph.Requests);
    }

    // ---- ListRoleAssignmentsAsync ----

    [Fact]
    public async Task ListRoleAssignmentsAsync_FollowsPagesAndKeepsOnlyUsers()
    {
        const string nextLink = $"https://graph.microsoft.com/v1.0/servicePrincipals/{TestOptions.AppServicePrincipalId}/appRoleAssignedTo?$skiptoken=page2";
        graph
            .Returns(HttpStatusCode.OK, $$"""
                {
                  "@odata.nextLink": "{{nextLink}}",
                  "value": [
                    { "principalId": "{{UserId}}", "principalType": "User", "appRoleId": "{{TestOptions.AgentRoleId}}" },
                    { "principalId": "0b5e8c1d-1111-4f00-9a00-0000000000aa", "principalType": "Group", "appRoleId": "{{TestOptions.AgentRoleId}}" }
                  ]
                }
                """)
            .Returns(HttpStatusCode.OK, $$"""
                {
                  "value": [
                    { "principalId": "0b5e8c1d-1111-4f00-9a00-000000000002", "principalType": "User", "appRoleId": "{{TestOptions.SupervisorRoleId}}" }
                  ]
                }
                """);

        var assignments = await CreateSut().ListRoleAssignmentsAsync(Token);

        Assert.Equal(
        [
            new UserRoleAssignment(UserId, TestOptions.AgentRoleId),
            new UserRoleAssignment("0b5e8c1d-1111-4f00-9a00-000000000002", TestOptions.SupervisorRoleId),
        ], assignments);
        Assert.Equal(2, graph.Requests.Count);
        Assert.Contains("$skiptoken=page2", graph.Requests[1].Query);
    }

    // ---- GetUsersAsync ----

    [Fact]
    public async Task GetUsersAsync_ManyIds_QueriesByChunksOf15()
    {
        var ids = Enumerable.Range(1, 20).Select(i => Guid.NewGuid().ToString()).ToList();
        graph
            .Returns(HttpStatusCode.OK, """{ "value": [ { "id": "a", "displayName": "A", "mail": "a@example.com" } ] }""")
            .Returns(HttpStatusCode.OK, """{ "value": [ { "id": "b", "displayName": "B", "mail": "b@example.com" } ] }""");

        var users = await CreateSut().GetUsersAsync(ids, Token);

        Assert.Equal(2, users.Count);
        Assert.Equal(2, graph.Requests.Count);
        Assert.Contains($"id in ('{ids[0]}',", graph.Requests[0].Query);
        Assert.Contains($"'{ids[14]}')", graph.Requests[0].Query);
        Assert.Contains($"id in ('{ids[15]}',", graph.Requests[1].Query);
    }

    // ---- GetUserAsync ----

    [Fact]
    public async Task GetUserAsync_ExistingUser_ReadsEmailFromTheEmailIdentity()
    {
        graph.Returns(HttpStatusCode.OK, $$"""
            {
              "id": "{{UserId}}",
              "displayName": "Jane",
              "mail": "other@example.com",
              "identities": [
                { "signInType": "userPrincipalName", "issuer": "x", "issuerAssignedId": "upn@x" },
                { "signInType": "emailAddress", "issuer": "x", "issuerAssignedId": "jane@example.com" }
              ]
            }
            """);

        var user = await CreateSut().GetUserAsync(UserId, Token);

        Assert.Equal(new UserRecord(UserId, "Jane", "jane@example.com"), user);
        Assert.Equal($"/v1.0/users/{UserId}", Assert.Single(graph.Requests).Path);
    }

    [Fact]
    public async Task GetUserAsync_NotFound_ReturnsNull()
    {
        graph.ReturnsError(HttpStatusCode.NotFound, "Request_ResourceNotFound", "Resource does not exist.");

        var user = await CreateSut().GetUserAsync(UserId, Token);

        Assert.Null(user);
    }

    // ---- GetAppRolesAsync ----

    [Fact]
    public async Task GetAppRolesAsync_ReadsTheWebAppRolesAndKeepsEnabledOnes()
    {
        graph.Returns(HttpStatusCode.OK, $$"""
            {
              "id": "{{TestOptions.AppServicePrincipalId}}",
              "appRoles": [
                { "id": "{{TestOptions.SupervisorRoleId}}", "value": "Supervisor", "isEnabled": true },
                { "id": "{{TestOptions.AgentRoleId}}", "value": "Agent", "isEnabled": true },
                { "id": "33333333-3333-3333-3333-333333333333", "value": "Retired", "isEnabled": false },
                { "id": "44444444-4444-4444-4444-444444444444", "value": null, "isEnabled": true }
              ]
            }
            """);

        var roles = await CreateSut().GetAppRolesAsync(Token);

        Assert.Equal(TestOptions.AppRoles, roles);
        var request = Assert.Single(graph.Requests);
        Assert.Equal($"/v1.0/servicePrincipals/{TestOptions.AppServicePrincipalId}", request.Path);
        Assert.Contains("$select=appRoles", request.Query);
    }

    [Fact]
    public async Task GetAppRolesAsync_GraphFails_ThrowsGraphError()
    {
        graph.ReturnsError(HttpStatusCode.Forbidden, "Authorization_RequestDenied", "Insufficient privileges.");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().GetAppRolesAsync(Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
    }

    // ---- GetRoleIdAsync ----

    [Fact]
    public async Task GetRoleIdAsync_ReturnsTheRoleOnTheWebAppOnly()
    {
        graph.Returns(HttpStatusCode.OK, $$"""
            {
              "value": [
                { "resourceId": "22222222-2222-2222-2222-222222222222", "appRoleId": "{{TestOptions.SupervisorRoleId}}" },
                { "resourceId": "{{TestOptions.AppServicePrincipalId}}", "appRoleId": "{{TestOptions.AgentRoleId}}" }
              ]
            }
            """);

        var roleId = await CreateSut().GetRoleIdAsync(UserId, Token);

        Assert.Equal(TestOptions.AgentRoleId, roleId);
        Assert.Equal($"/v1.0/users/{UserId}/appRoleAssignments", Assert.Single(graph.Requests).Path);
    }

    [Fact]
    public async Task GetRoleIdAsync_NoAssignmentOnTheWebApp_ReturnsNull()
    {
        graph.Returns(HttpStatusCode.OK, """{ "value": [] }""");

        var roleId = await CreateSut().GetRoleIdAsync(UserId, Token);

        Assert.Null(roleId);
    }

    // ---- DeleteUserAsync ----

    [Fact]
    public async Task DeleteUserAsync_ExistingUser_SendsDelete()
    {
        graph.Returns(HttpStatusCode.NoContent);

        await CreateSut().DeleteUserAsync(UserId, Token);

        var request = Assert.Single(graph.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/v1.0/users/{UserId}", request.Path);
    }

    [Fact]
    public async Task DeleteUserAsync_NotFound_ThrowsUserNotFound()
    {
        graph.ReturnsError(HttpStatusCode.NotFound, "Request_ResourceNotFound", "Resource does not exist.");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().DeleteUserAsync(UserId, Token));

        Assert.Equal(PartnerUserErrorCode.UserNotFound, exception.Code);
    }

    // ---- Error mapping ----

    [Fact]
    public async Task AnyCall_GraphDeniesAccess_ThrowsGraphErrorWithStatus()
    {
        graph.ReturnsError(HttpStatusCode.Forbidden, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().ListRoleAssignmentsAsync(Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        Assert.Contains("HTTP 403", exception.Message);
        Assert.Contains("Authorization_RequestDenied", exception.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_ServerError_ThrowsGraphError()
    {
        graph.ReturnsError(HttpStatusCode.InternalServerError, "generalException", "Something went wrong.");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().DeleteUserAsync(UserId, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
    }

    [Fact]
    public async Task AnyCall_Cancelled_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().EmailExistsAsync("jane@example.com", cancellation.Token));
    }
}
