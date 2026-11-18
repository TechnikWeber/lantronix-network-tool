using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Lantronix.Discovery;

/// <summary>A device that answered the discovery query.</summary>
/// <param name="Address">Where the reply came from.</param>
/// <param name="Mac">The MAC address from the reply.</param>
/// <param name="Network">The local network the device's address lies in; null when it is in none of them.</param>
/// <param name="Reply">The complete reply, for whoever wants to dig into bytes 4..23.</param>
/// <param name="Seen">When the reply arrived.</param>
public sealed record DiscoveredDevice(
    IPAddress Address,
    PhysicalAddress Mac,
    LocalNetwork? Network,
    byte[] Reply,
    DateTimeOffset Seen)
{
    public string MacText => Lantronix.Discovery.Mac.Format(Mac);

    /// <summary>
    /// True when this computer can talk to the device directly. A device configured for
    /// another subnet still answers the broadcast, but its web interface is out of reach
    /// until it gets a matching address.
    /// </summary>
    public bool Reachable => Network != null || IPAddress.IsLoopback(Address);

    /// <summary>0.0.0.0: the device has no IP address yet (DHCP failed or never configured).</summary>
    public bool HasNoAddress => Address.Equals(IPAddress.Any);
}

public sealed class ScanOptions
{
    /// <summary>How long to wait for replies after the last query went out.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Send the query as broadcast (to 255.255.255.255 and each subnet's broadcast address).</summary>
    public bool Broadcast { get; set; } = true;

    /// <summary>Addresses to query directly (also across routers and through firewalls that drop broadcast replies).</summary>
    public IReadOnlyList<IPAddress> Targets { get; set; } = [];

    /// <summary>Broadcast rounds; UDP gets lost, so the query is repeated a few times.</summary>
    public int Rounds { get; set; } = 3;

    public TimeSpan RoundInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Only report devices whose MAC contains these hex digits (see <see cref="Mac.Matches"/>).</summary>
    public string? MacFilter { get; set; }

    /// <summary>Stop at the first matching device.</summary>
    public bool StopAtFirst { get; set; }

    /// <summary>Destination port; only tests and simulators change it.</summary>
    public int Port { get; set; } = Protocol.Port;

    /// <summary>Gets problems that do not stop the scan (an interface that refuses to send, …).</summary>
    public Action<string>? Warning { get; set; }
}

/// <summary>Finds Lantronix devices.</summary>
public static class Scanner
{
    /// <summary>
    /// Sends the discovery query and yields every device once, as soon as it answers.
    /// Ends after <see cref="ScanOptions.Timeout"/> without new queries, or when cancelled.
    /// </summary>
    public static async IAsyncEnumerable<DiscoveredDevice> ScanAsync(ScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var networks = LocalNetworks.Get();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<(IPEndPoint From, byte[] Data)>();
        var sockets = OpenSockets(networks, options.Warning);
        var receivers = sockets.Select(s => Task.Run(() => ReceiveLoop(s.Socket, channel.Writer, stop.Token))).ToArray();
        var sender = Task.Run(async () =>
        {
            try
            {
                await SendQueries(sockets, options, stop.Token, options.Targets);
                // Long waits: repeat the broadcast every 2 s. Catches devices that are switched
                // on during the search, and Windows' firewall lets replies to a broadcast in
                // for about 3 s only.
                var end = DateTime.UtcNow + options.Timeout;
                while (DateTime.UtcNow < end)
                {
                    var left = end - DateTime.UtcNow;
                    await Task.Delay(left < RepeatEvery ? left : RepeatEvery, stop.Token);
                    if (options.Broadcast && end - DateTime.UtcNow > TimeSpan.FromSeconds(1))
                        await SendQueries(sockets, options, stop.Token, [], rounds: 1);
                }
            }
            catch (OperationCanceledException) { }
            finally { stop.Cancel(); }
        });
        _ = Task.WhenAll(receivers).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        var seen = new HashSet<(string, string)>();
        try
        {
            await foreach (var (from, data) in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var mac = Protocol.ParseReply(data);
                if (mac == null || !Mac.Matches(mac, options.MacFilter)) continue;
                if (!seen.Add((from.Address.ToString(), mac.ToString()))) continue;
                yield return new DiscoveredDevice(from.Address, mac, LocalNetworks.Find(networks, from.Address),
                    data, DateTimeOffset.Now);
                if (options.StopAtFirst) yield break;
            }
        }
        finally
        {
            stop.Cancel();
            foreach (var s in sockets) s.Socket.Dispose();
            try { await sender; } catch { /* already reported or cancelled */ }
        }
    }

    /// <summary>Collects the whole scan into a list (sorted by IP address).</summary>
    public static async Task<List<DiscoveredDevice>> ScanAllAsync(ScanOptions options, CancellationToken cancellationToken = default)
    {
        var list = new List<DiscoveredDevice>();
        await foreach (var d in ScanAsync(options, cancellationToken)) list.Add(d);
        list.Sort((a, b) => IPv4.ToUInt(a.Address).CompareTo(IPv4.ToUInt(b.Address)));
        return list;
    }

    private static readonly TimeSpan RepeatEvery = TimeSpan.FromSeconds(2);

    private sealed record SocketInfo(Socket Socket, LocalNetwork? Network);

    // One socket on 0.0.0.0 for 255.255.255.255 and the unicast targets, plus one per local
    // address: Windows sends a limited broadcast out of a single interface only, so with
    // LAN + Wi-Fi + VPN a device behind the "wrong" adapter would never hear the query.
    private static List<SocketInfo> OpenSockets(IReadOnlyList<LocalNetwork> networks, Action<string>? warn)
    {
        var list = new List<SocketInfo> { new(NewSocket(IPAddress.Any), null) };
        foreach (var n in networks)
        {
            try { list.Add(new(NewSocket(n.Address), n)); }
            catch (SocketException e) { warn?.Invoke($"{n}: {e.Message}"); }
        }
        return list;
    }

    private static Socket NewSocket(IPAddress bind)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
        if (OperatingSystem.IsWindows())
        {
            // otherwise an ICMP "port unreachable" from a unicast target kills ReceiveFrom
            const int SIO_UDP_CONNRESET = -1744830452;
            s.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
        }
        s.Bind(new IPEndPoint(bind, 0));
        return s;
    }

    private static async Task SendQueries(List<SocketInfo> sockets, ScanOptions o, CancellationToken ct,
        IReadOnlyList<IPAddress> targets, int? rounds = null)
    {
        var query = Protocol.Query.ToArray();
        var any = sockets[0].Socket;
        var warned = new HashSet<string>();

        async Task Send(Socket s, IPAddress to, string? what)
        {
            try { await s.SendToAsync(query, SocketFlags.None, new IPEndPoint(to, o.Port), ct); }
            catch (SocketException e)
            {
                if (what != null && warned.Add(what)) o.Warning?.Invoke($"{what}: {e.Message}");
            }
        }

        var n = rounds ?? (o.Broadcast ? Math.Max(1, o.Rounds) : 1);
        for (var round = 0; round < n; round++)
        {
            if (round > 0) await Task.Delay(o.RoundInterval, ct);
            if (o.Broadcast)
            {
                await Send(any, IPAddress.Broadcast, "255.255.255.255");
                foreach (var s in sockets.Skip(1))
                {
                    // tunnels (VPN, WireGuard) refuse broadcasts — expected, so no warning
                    await Send(s.Socket, IPAddress.Broadcast, null);
                    if (s.Network!.PrefixLength < 31)
                        await Send(s.Socket, s.Network.Broadcast, $"{s.Network} → {s.Network.Broadcast}");
                }
            }
            // unicast targets once per round, too (only the first round when not broadcasting)
            var i = 0;
            foreach (var t in targets)
            {
                await Send(any, t, t.ToString());
                if (++i % 64 == 0) await Task.Delay(5, ct); // a /24 takes ~20 ms, no burst of 4096 packets
            }
        }
    }

    private static async Task ReceiveLoop(Socket s, ChannelWriter<(IPEndPoint, byte[])> writer, CancellationToken ct)
    {
        var buffer = new byte[1500];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = await s.ReceiveFromAsync(buffer, SocketFlags.None, any, ct);
                writer.TryWrite(((IPEndPoint)r.RemoteEndPoint, buffer.AsSpan(0, r.ReceivedBytes).ToArray()));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize) { }
            catch (SocketException) { break; }
        }
    }
}
