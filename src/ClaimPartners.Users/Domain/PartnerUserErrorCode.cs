namespace ClaimPartners.Users.Domain;

/// <summary>
/// Reason of a <see cref="PartnerUserException"/>.
/// </summary>
public enum PartnerUserErrorCode
{
    /// <summary>A user with the same email already exists in the tenant.</summary>
    UserAlreadyExists,

    /// <summary>The user does not exist in the tenant.</summary>
    UserNotFound,

    /// <summary>The role is not a key of the configured roles map.</summary>
    UnknownRole,

    /// <summary>Microsoft Graph or the authentication to it failed.</summary>
    GraphError,
}
