using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lantronix.Discovery;

namespace LantronixScan;

public static class Program
{
    private const string Usage = """
        lantronix-scan — find Lantronix devices on the local network (UDP 30718)

        Usage:
          lantronix-scan [scan] [options]
          lantronix-scan assign --mac MAC --ip IP [--interface NAME] [--force]

        Scan options:
          -t, --timeout SEC     wait this long for replies (default 3)
          -m, --mac FILTER      only devices whose MAC contains these digits ("2F-1A", "2f1a" …)
              --target LIST     also ask these addresses directly: 192.168.1.20,
                                192.168.1.0/24, 192.168.1.10-50 (repeatable)
              --no-broadcast    only ask the --target addresses
              --first           stop at the first device found
              --json            JSON array (ip, mac, interface, reachable, reply)
              --csv             CSV with header
              --port N          destination port (default 30718; for simulators)

        assign: gives the device a temporary IP address (ARP + port 1). Needs administrator
        rights; asks via sudo/UAC if not run as root. Lasts until the device restarts.

        Exit code: 0 device(s) found / assigned, 1 nothing found / failed, 2 usage error.
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var list = args.ToList();
            if (list.Any(a => a is "-h" or "--help" or "help")) { Console.WriteLine(Usage); return 0; }
            if (list.Any(a => a is "-V" or "--version")) { Console.WriteLine(Version()); return 0; }
            if (list.Count > 0 && list[0] == "assign") return await Assign(list.Skip(1).ToList());
            if (list.Count > 0 && list[0] == "scan") list.RemoveAt(0);
            return await Scan(list);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"lantronix-scan: {e.Message}");
            Console.Error.WriteLine("Try 'lantronix-scan --help'.");
            return 2;
        }
    }

    private static string Version()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "?";

    private sealed class UsageException(string message) : Exception(message);

    private static string Value(List<string> args, ref int i)
    {
        var name = args[i];
        if (++i >= args.Count) throw new UsageException($"{name} needs a value");
        return args[i];
    }

    private static async Task<int> Scan(List<string> args)
    {
        var o = new ScanOptions { Warning = w => Console.Error.WriteLine($"warning: {w}") };
        var targets = new List<IPAddress>();
        string format = "text";
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-t" or "--timeout":
                    if (!double.TryParse(Value(args, ref i), System.Globalization.CultureInfo.InvariantCulture, out var sec) || sec <= 0 || sec > 600)
                        throw new UsageException("--timeout: seconds between 0 and 600");
                    o.Timeout = TimeSpan.FromSeconds(sec);
                    break;
                case "-m" or "--mac":
                    o.MacFilter = Value(args, ref i);
                    if (!Mac.IsValidFilter(o.MacFilter)) throw new UsageException($"--mac: '{o.MacFilter}' is not part of a MAC address");
                    break;
                case "--target":
                    if (!AddressRange.TryParse(Value(args, ref i), out var t, out var err)) throw new UsageException($"--target: {err}");
                    targets.AddRange(t);
                    break;
                case "--no-broadcast": o.Broadcast = false; break;
                case "--first": o.StopAtFirst = true; break;
                case "--json": format = "json"; break;
                case "--csv": format = "csv"; break;
                case "--port":
                    if (!int.TryParse(Value(args, ref i), out var port) || port is < 1 or > 65535) throw new UsageException("--port: 1..65535");
                    o.Port = port;
                    break;
                default: throw new UsageException($"unknown option '{args[i]}'");
            }
        }
        if (targets.Count > AddressRange.MaxAddresses) throw new UsageException($"more than {AddressRange.MaxAddresses} target addresses");
        o.Targets = targets.Distinct().ToList();
        if (!o.Broadcast && o.Targets.Count == 0) throw new UsageException("--no-broadcast needs --target");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var found = new List<DiscoveredDevice>();
        if (format == "text")
        {
            Console.Error.WriteLine($"Searching for {o.Timeout.TotalSeconds:0.#} s …");
            Console.WriteLine($"{"IP address",-16} {"MAC address",-18} Network");
        }
        try
        {
            await foreach (var d in Scanner.ScanAsync(o, cts.Token))
            {
                found.Add(d);
                if (format == "text") Console.WriteLine($"{d.Address,-16} {d.MacText,-18} {Where(d)}");
            }
        }
        catch (OperationCanceledException) { }

        found.Sort((a, b) => IPv4.ToUInt(a.Address).CompareTo(IPv4.ToUInt(b.Address)));
        switch (format)
        {
            case "json":
                Console.WriteLine(JsonSerializer.Serialize(found.Select(ToJson).ToList(), JsonContext.Default.ListDeviceJson));
                break;
            case "csv":
                Console.WriteLine("ip,mac,interface,reachable,reply");
                foreach (var d in found)
                    Console.WriteLine($"{d.Address},{d.MacText},\"{d.Network?.InterfaceName?.Replace("\"", "\"\"")}\",{(d.Reachable ? "true" : "false")},{Convert.ToHexString(d.Reply)}");
                break;
            default:
                Console.Error.WriteLine(found.Count switch { 0 => "No device found.", 1 => "1 device found.", var n => $"{n} devices found." });
                if (found.Count == 0 && Firewall.Hint() is { } hint) Console.Error.WriteLine(hint);
                break;
        }
        return found.Count > 0 ? 0 : 1;
    }

    private static string Where(DiscoveredDevice d)
        => d.HasNoAddress ? "no IP address yet — use 'assign'"
            : d.Network != null ? d.Network.InterfaceName
            : IPAddress.IsLoopback(d.Address) ? "loopback"
            : "other subnet — not reachable from here";

    private static async Task<int> Assign(List<string> args)
    {
        string? macText = null, ipText = null, ifName = null;
        var force = false;
        var port = Protocol.Port;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--mac" or "-m": macText = Value(args, ref i); break;
                case "--ip": ipText = Value(args, ref i); break;
                case "--interface" or "-i": ifName = Value(args, ref i); break;
                case "--force": force = true; break;
                case "--port": port = int.Parse(Value(args, ref i)); break;
                default: throw new UsageException($"unknown option '{args[i]}'");
            }
        }
        if (macText == null || !Mac.TryParse(macText, out var mac)) throw new UsageException("assign needs --mac with a full MAC address");
        if (ipText == null || !IPAddress.TryParse(ipText, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new UsageException("assign needs --ip with an IPv4 address");

        var nets = LocalNetworks.Get();
        var net = ifName != null
            ? nets.FirstOrDefault(n => n.InterfaceName == ifName && n.Contains(ip))
            : LocalNetworks.Find(nets, ip);
        if (net == null)
        {
            Console.Error.WriteLine($"{ip} is in none of this computer's networks:");
            foreach (var n in nets) Console.Error.WriteLine($"  {n}");
            Console.Error.WriteLine("The new address has to be in the same subnet as this computer.");
            return 1;
        }

        Console.Error.WriteLine($"Assigning {ip} to {Mac.Format(mac)} via {net} …");
        var result = await IpAssigner.AssignAsync(mac, ip, net, Elevation.Terminal,
            new Progress<AssignStep>(s => Console.Error.WriteLine(s switch
            {
                AssignStep.Checking => $"  checking whether {ip} is free …",
                AssignStep.Assigning => "  setting the ARP entry and knocking on port 1 …",
                _ => $"  waiting for the device at {ip} …",
            })), force, port);
        Console.WriteLine(result.Message);
        if (result.Success)
            Console.WriteLine($"Web interface: http://{ip}/   Setup mode: telnet {ip} 9999");
        return result.Success ? 0 : 1;
    }

    private static DeviceJson ToJson(DiscoveredDevice d) => new(d.Address.ToString(), d.MacText,
        d.Network?.InterfaceName, d.Network?.Address.ToString(), d.Reachable, Convert.ToHexString(d.Reply));
}

public sealed record DeviceJson(
    [property: JsonPropertyName("ip")] string Ip,
    [property: JsonPropertyName("mac")] string Mac,
    [property: JsonPropertyName("interface")] string? Interface,
    [property: JsonPropertyName("localAddress")] string? LocalAddress,
    [property: JsonPropertyName("reachable")] bool Reachable,
    [property: JsonPropertyName("reply")] string Reply);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DeviceJson>))]
internal partial class JsonContext : JsonSerializerContext;
