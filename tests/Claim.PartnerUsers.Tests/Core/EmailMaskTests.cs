using Claim.PartnerUsers.Core;

namespace Claim.PartnerUsers.Tests.Core;

public sealed class EmailMaskTests
{
    [Theory]
    [InlineData("john.doe@contoso.com", "j***@contoso.com")]
    [InlineData("a@b.c", "a***@b.c")]
    [InlineData("@contoso.com", "***")]
    [InlineData("not-an-email", "***")]
    public void Mask_Email_HidesTheLocalPart(string email, string expected)
    {
        Assert.Equal(expected, EmailMask.Mask(email));
    }
}
