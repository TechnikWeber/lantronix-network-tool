using System.Net.NetworkInformation;
using System.Text;

namespace Lantronix.Discovery;

/// <summary>MAC address helpers: lenient parsing and the formats people expect.</summary>
public static class Mac
{
    /// <summary>Lantronix' own OUI (00-80-A3), the start of most of their MAC addresses.</summary>
    public const string LantronixOui = "0080A3";

    /// <summary>Only the hex digits, upper case: "00:80:a3-be.2f1a" becomes "0080A3BE2F1A".</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            else if (!IsSeparator(c)) return "";
        return sb.ToString();
    }

    private static bool IsSeparator(char c) => c is ':' or '-' or '.' or ' ' or '_';

    /// <summary>Parses a full MAC address in any common notation.</summary>
    public static bool TryParse(string text, out PhysicalAddress mac)
    {
        var hex = Normalize(text);
        mac = PhysicalAddress.None;
        if (hex.Length != 12) return false;
        mac = new PhysicalAddress(Convert.FromHexString(hex));
        return true;
    }

    /// <summary>"00-80-A3-BE-2F-1A" (Windows style, also what Lantronix prints on the label).</summary>
    public static string Format(PhysicalAddress mac, char separator = '-')
        => string.Join(separator, mac.GetAddressBytes().Select(b => b.ToString("X2")));

    /// <summary>
    /// True when the MAC contains the filter's hex digits, in any notation:
    /// "2F-1A", "2f1a" and "BE:2F:1A" all match 00-80-A3-BE-2F-1A. An empty filter matches all.
    /// </summary>
    public static bool Matches(PhysicalAddress mac, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = Normalize(filter);
        return f.Length > 0 && Convert.ToHexString(mac.GetAddressBytes()).Contains(f, StringComparison.Ordinal);
    }

    /// <summary>True when the filter text can be used at all (hex digits and separators only).</summary>
    public static bool IsValidFilter(string? filter)
        => string.IsNullOrWhiteSpace(filter) || Normalize(filter).Length is > 0 and <= 12;
}
