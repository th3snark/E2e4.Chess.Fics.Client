using System.Globalization;

namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// Number parsing for server text. Always invariant-culture (the wire format does not
/// follow the user's locale) and never throws.
/// </summary>
internal static class FicsNumber
{
    public static bool TryParse(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
