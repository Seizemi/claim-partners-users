namespace ClaimPartners.Users.Domain;

/// <summary>
/// Thrown by <c>IPartnerUserService</c> when an operation cannot be completed.
/// </summary>
public sealed class PartnerUserException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="code">Reason of the failure.</param>
    /// <param name="message">Description safe to log (no email in full, no credential).</param>
    /// <param name="innerException">Underlying error, if any.</param>
    public PartnerUserException(PartnerUserErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Reason of the failure.
    /// </summary>
    public PartnerUserErrorCode Code { get; }
}
