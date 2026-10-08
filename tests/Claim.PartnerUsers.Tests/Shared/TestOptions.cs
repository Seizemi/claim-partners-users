using Claim.PartnerUsers.Configuration;

namespace Claim.PartnerUsers.Tests.Shared;

internal static class TestOptions
{
    public static readonly Guid SupervisorRoleId = Guid.Parse("c8e346c2-7745-4b10-9754-fd9e7566363d");
    public static readonly Guid AgentRoleId = Guid.Parse("7a412abe-ac73-4ed9-ad1e-5c3c8fceef53");
    public const string AppServicePrincipalId = "11111111-2222-3333-4444-555555555555";
    public const string TenantDomain = "claimdev.onmicrosoft.com";

    public static PartnerUserOptions Create() => new()
    {
        TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        TenantDomain = TenantDomain,
        GraphClientId = "99999999-8888-7777-6666-555555555555",
        GraphClientSecret = "test-secret",
        AppServicePrincipalId = AppServicePrincipalId,
        Roles = new Dictionary<string, Guid>
        {
            ["Supervisor"] = SupervisorRoleId,
            ["Agent"] = AgentRoleId,
        },
    };

    /// <summary>
    /// Configuration keys equivalent to <see cref="Create"/>, as the host would provide them.
    /// </summary>
    public static Dictionary<string, string?> AsConfiguration() => new()
    {
        ["PartnerUsers:TenantId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        ["PartnerUsers:TenantDomain"] = TenantDomain,
        ["PartnerUsers:GraphClientId"] = "99999999-8888-7777-6666-555555555555",
        ["PartnerUsers:GraphClientSecret"] = "test-secret",
        ["PartnerUsers:AppServicePrincipalId"] = AppServicePrincipalId,
        ["PartnerUsers:Roles:Supervisor"] = SupervisorRoleId.ToString(),
        ["PartnerUsers:Roles:Agent"] = AgentRoleId.ToString(),
        ["PartnerUsers:AssignableRoles:0"] = "Agent",
    };
}
