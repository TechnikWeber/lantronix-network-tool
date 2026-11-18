using System.Net;
using System.Net.Sockets;

namespace Lantronix.Discovery;

/// <summary>
/// Parses the targets for a unicast scan: "192.168.1.20", "192.168.1.0/24",
/// "192.168.1.10-192.168.1.50" or "192.168.1.10-50".
/// </summary>
public static class AddressRange
{
    /// <summary>More than this many addresses per scan is refused (a /20).</summary>
    public const int MaxAddresses = 4096;

    public static bool TryParse(string text, out IReadOnlyList<IPAddress> addresses, out string? error)
    {
        addresses = [];
        error = null;
        var list = new List<IPAddress>();
        foreach (var raw in text.Split([',', ';', ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseOne(raw.Trim(), list, out error)) return false;
            if (list.Count > MaxAddresses)
            {
                error = $"more than {MaxAddresses} addresses";
                return false;
            }
        }
        addresses = list.Distinct().ToList();
        return true;
    }

    private static bool TryParseOne(string s, List<IPAddress> into, out string? error)
    {
        error = null;
        uint first, last;
        var slash = s.IndexOf('/');
        var dash = s.IndexOf('-');
        if (slash > 0)
        {
            if (!TryV4(s[..slash], out var net) || !int.TryParse(s[(slash + 1)..], out var prefix) || prefix is < 0 or > 32)
                return Fail(s, out error);
            if (prefix < 32 - 12) { error = $"{s}: network too large"; return false; }
            var mask = IPv4.ToUInt(IPv4.MaskFromPrefix(prefix));
            first = IPv4.ToUInt(net) & mask;
            last = first | ~mask;
            if (prefix <= 30) { first++; last--; } // without network and broadcast address
        }
        else if (dash > 0)
        {
            if (!TryV4(s[..dash], out var a)) return Fail(s, out error);
            var right = s[(dash + 1)..];
            IPAddress b;
            if (byte.TryParse(right, out var lastOctet))
            {
                var bytes = a.GetAddressBytes();
                bytes[3] = lastOctet;
                b = new IPAddress(bytes);
            }
            else if (!TryV4(right, out b)) return Fail(s, out error);
            first = IPv4.ToUInt(a);
            last = IPv4.ToUInt(b);
            if (last < first) (first, last) = (last, first);
        }
        else
        {
            if (!TryV4(s, out var ip)) return Fail(s, out error);
            first = last = IPv4.ToUInt(ip);
        }

        if ((ulong)last - first + 1 > MaxAddresses) { error = $"{s}: more than {MaxAddresses} addresses"; return false; }
        for (var v = (ulong)first; v <= last; v++) into.Add(IPv4.FromUInt((uint)v));
        return true;
    }

    private static bool TryV4(string s, out IPAddress ip)
    {
        // IPAddress.TryParse also accepts "10" or "1.2" — demand four parts
        if (s.Count(c => c == '.') == 3 && IPAddress.TryParse(s, out var p) && p.AddressFamily == AddressFamily.InterNetwork)
        {
            ip = p;
            return true;
        }
        ip = IPAddress.None;
        return false;
    }

    private static bool Fail(string s, out string? error)
    {
        error = $"{s}: not an IPv4 address, range or network";
        return false;
    }
}
