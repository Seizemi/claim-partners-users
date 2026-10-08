namespace Claim.PartnerUsers.Core;

/// <summary>
/// Masks an email for logs: <c>john.doe@contoso.com</c> becomes <c>j***@contoso.com</c>.
/// </summary>
internal static class EmailMask
{
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
