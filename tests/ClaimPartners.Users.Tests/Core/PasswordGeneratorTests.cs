using ClaimPartners.Users.Core;

namespace ClaimPartners.Users.Tests.Core;

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generate_Always_ReturnsLongPasswordWithEveryCharacterCategory()
    {
        for (var i = 0; i < 100; i++)
        {
            var password = PasswordGenerator.Generate();

            Assert.Equal(PasswordGenerator.Length, password.Length);
            Assert.Contains(password, PasswordGenerator.Upper.Contains);
            Assert.Contains(password, PasswordGenerator.Lower.Contains);
            Assert.Contains(password, PasswordGenerator.Digits.Contains);
            Assert.Contains(password, PasswordGenerator.Symbols.Contains);
        }
    }

    [Fact]
    public void Generate_TwoCalls_ReturnDifferentPasswords()
    {
        Assert.NotEqual(PasswordGenerator.Generate(), PasswordGenerator.Generate());
    }
}
