using System.Net;
using System.Text.Json;
using Claim.PartnerUsers.Core;
using Claim.PartnerUsers.Domain;
using Claim.PartnerUsers.Providers.Graph;
using Claim.PartnerUsers.Tests.Shared;
using Microsoft.Extensions.Options;

namespace Claim.PartnerUsers.Tests.Providers.Graph;

public sealed class GraphPartnerUserProviderTests
{
    private const string UserId = "0b5e8c1d-1111-4f00-9a00-000000000001";
    private readonly StubGraphHandler graph = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private GraphPartnerUserProvider CreateSut() => new(graph.CreateClient(), Options.Create(TestOptions.Create()));

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
