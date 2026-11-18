using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lantronix.Discovery;

/// <summary>An IPv4 address of this computer, with its subnet.</summary>
public sealed record LocalNetwork(string InterfaceName, string InterfaceId, int InterfaceIndex,
    IPAddress Address, IPAddress Mask)
{
    /// <summary>The default gateways of this interface (IPv4).</summary>
    public IReadOnlyList<IPAddress> Gateways { get; init; } = [];

    public int PrefixLength => IPv4.PrefixLength(Mask);
    public IPAddress Broadcast => IPv4.FromUInt(IPv4.ToUInt(Address) | ~IPv4.ToUInt(Mask));
    public bool Contains(IPAddress ip) => IPv4.SameSubnet(Address, ip, Mask);
    public override string ToString() => $"{InterfaceName} ({Address}/{PrefixLength})";
}

public static class LocalNetworks
{
    /// <summary>All IPv4 networks on interfaces that are up, except loopback.</summary>
    public static IReadOnlyList<LocalNetwork> Get()
    {
        var list = new List<LocalNetwork>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return list; }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up &&
                nic.OperationalStatus != OperationalStatus.Unknown) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties props;
            int index = -1;
            try
            {
                props = nic.GetIPProperties();
                index = props.GetIPv4Properties()?.Index ?? -1;
            }
            catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException) { continue; }

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;
                var mask = ua.IPv4Mask;
                if (mask == null || mask.Equals(IPAddress.Any)) mask = IPv4.MaskFromPrefix(ua.PrefixLength);
                var gateways = props.GatewayAddresses.Select(g => g.Address)
                    .Where(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any)).ToList();
                list.Add(new LocalNetwork(nic.Name, nic.Id, index, ua.Address, mask) { Gateways = gateways });
            }
        }
        return list;
    }

    /// <summary>The local network an address lies in, or null.</summary>
    public static LocalNetwork? Find(IEnumerable<LocalNetwork> networks, IPAddress ip)
        => networks.Where(n => n.Contains(ip)).OrderByDescending(n => n.PrefixLength).FirstOrDefault();
}

/// <summary>Small IPv4 arithmetic helpers.</summary>
public static class IPv4
{
    public static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    public static IPAddress FromUInt(uint v)
        => new([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);

    public static IPAddress MaskFromPrefix(int prefix)
        => FromUInt(prefix <= 0 ? 0u : prefix >= 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix));

    public static int PrefixLength(IPAddress mask) => System.Numerics.BitOperations.PopCount(ToUInt(mask));

    public static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        if (a.AddressFamily != AddressFamily.InterNetwork || b.AddressFamily != AddressFamily.InterNetwork) return false;
        var m = ToUInt(mask);
        return (ToUInt(a) & m) == (ToUInt(b) & m);
    }
}
