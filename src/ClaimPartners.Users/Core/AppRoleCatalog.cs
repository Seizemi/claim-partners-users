namespace ClaimPartners.Users.Core;

/// <summary>
/// The web application's app roles, read from Entra the first time an operation needs them and then kept for the
/// lifetime of the application (a role added in Entra is seen after a restart). Roles are matched by value, so their
/// IDs never have to be configured and may differ between tenants. A failed or cancelled read is not cached: the
/// next operation tries again.
/// </summary>
internal sealed class AppRoleCatalog(IPartnerUserProvider provider)
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private volatile AppRoleMap? roles;

    public async Task<AppRoleMap> GetAsync(CancellationToken cancellationToken)
    {
        if (roles is { } loaded)
        {
            return loaded;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            // Concurrent first calls wait here; only the first one reads Graph.
            return roles ??= new AppRoleMap(await provider.GetAppRolesAsync(cancellationToken));
        }
        finally
        {
            loadLock.Release();
        }
    }
}

internal sealed class AppRoleMap
{
    private readonly Dictionary<string, Guid> idByValue;
    private readonly Dictionary<Guid, string> valueById;

    public AppRoleMap(IEnumerable<AppRoleDefinition> roles)
    {
        // Entra keeps values and IDs unique per application; DistinctBy only guards against a malformed response.
        var distinct = roles.DistinctBy(role => role.Value, StringComparer.Ordinal).DistinctBy(role => role.Id).ToList();
        idByValue = distinct.ToDictionary(role => role.Value, role => role.Id, StringComparer.Ordinal);
        valueById = distinct.ToDictionary(role => role.Id, role => role.Value);
    }

    /// <summary>ID of the role with this value (case-sensitive, as in the <c>roles</c> claim).</summary>
    public bool TryGetId(string value, out Guid id) => idByValue.TryGetValue(value, out id);

    /// <summary>Value of the role with this ID, or <c>null</c> for an ID the application doesn't define.</summary>
    public string? ValueOf(Guid id) => valueById.GetValueOrDefault(id);
}
