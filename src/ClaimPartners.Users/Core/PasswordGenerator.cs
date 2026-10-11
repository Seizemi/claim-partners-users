using System.Security.Cryptography;

namespace ClaimPartners.Users.Core;

/// <summary>
/// Throwaway password set at creation and never returned, shown or logged.
/// Contains every character category so it satisfies the tenant's complexity rules.
/// </summary>
internal static class PasswordGenerator
{
    public const int Length = 40;
    public const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Lower = "abcdefghijkmnopqrstuvwxyz";
    public const string Digits = "23456789";
    public const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Generate()
    {
        var chars = new char[Length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < Length; i++)
        {
            chars[i] = Pick(All);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}
