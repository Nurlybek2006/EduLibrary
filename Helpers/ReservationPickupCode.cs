using System.Security.Cryptography;
using System.Text;

namespace EduLibrary.Helpers;

public static class ReservationPickupCode
{
    public static string Generate() => "ELR-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static string? Normalize(string? code)
    {
        if (code is null || code.Length > 100) return null;
        var value = code.Trim().ToUpperInvariant();
        return value.Length == 36 && value.StartsWith("ELR-", StringComparison.Ordinal)
            && value.AsSpan(4).IndexOfAnyExcept("0123456789ABCDEF") < 0 ? value : null;
    }

    public static bool Matches(string? supplied, string expected)
    {
        var normalized = Normalize(supplied);
        return normalized is not null && Normalize(expected) is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalized), Encoding.ASCII.GetBytes(expected));
    }
}
