using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace Lantronix.Discovery;

/// <summary>How to get the administrator rights a static ARP entry needs.</summary>
public enum Elevation
{
    /// <summary>Run the commands as they are (the process already has the rights, or fail).</summary>
    None,
    /// <summary>sudo in the terminal (Linux, macOS); on Windows the UAC prompt.</summary>
    Terminal,
    /// <summary>A password dialog: pkexec (Linux), the macOS admin prompt, UAC (Windows).</summary>
    Graphical,
}

public enum AssignStatus { Done, AlreadyThere, NotInNetwork, UnusableAddress, InUse, ArpFailed, Cancelled, NoAnswer, Failed }

public enum AssignStep { Checking, Assigning, Verifying }

/// <summary>Outcome of <see cref="IpAssigner.AssignAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Ip">The address that was to be assigned.</param>
/// <param name="Detail">Error output of the commands, if any.</param>
/// <param name="Device">The device as it answered at the new address.</param>
public sealed record AssignResult(AssignStatus Status, IPAddress Ip, string Detail = "", DiscoveredDevice? Device = null)
{
    public bool Success => Status is AssignStatus.Done or AssignStatus.AlreadyThere;

    /// <summary>English text for the result (the GUI has its own translations).</summary>
    public string Message => Status switch
    {
        AssignStatus.Done => $"Done: the device now answers at {Ip} (until it restarts).",
        AssignStatus.AlreadyThere => $"The device already answers at {Ip}.",
        AssignStatus.NotInNetwork => $"{Ip} is not in the chosen network.",
        AssignStatus.UnusableAddress => $"{Ip} cannot be used (own, network or broadcast address).",
        AssignStatus.InUse => $"{Ip} is already in use (gateway, or it answers ping). Choose another address.",
        AssignStatus.ArpFailed => "Could not set the ARP entry (administrator rights needed?)",
        AssignStatus.Cancelled => "Cancelled (no administrator rights given).",
        AssignStatus.NoAnswer => $"The device does not answer at {Ip}. Check the MAC address; some models accept " +
                                 "the ARP method only while they have no IP address, Evolution OS devices not at all.",
        _ => "Failed.",
    } + (Detail.Length > 0 ? $" ({Detail})" : "");
}

/// <summary>
/// Gives a device a temporary IP address the classic Lantronix way: a static ARP entry
/// (IP → the device's MAC) on this computer, then a TCP connection attempt to port 1.
/// The device takes over the IP address the packet was sent to. The connection itself
/// fails, that is expected. The address only lasts until the device restarts; set it
/// permanently in the web interface or in setup mode (telnet port 9999).
/// </summary>
/// <remarks>
/// Works with the "CoBos"/standard firmware (XPort, UDS, EDS, MSS, WiPort …); on some
/// models only as long as the device has no IP address (0.0.0.0). Evolution OS devices
/// (XPort EDGE, EDS3000, PremierWave) may ignore it.
/// </remarks>
public static class IpAssigner
{
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>The commands that will run, for showing them before asking for a password.</summary>
    public static string Script(PhysicalAddress mac, IPAddress ip, LocalNetwork network)
    {
        if (OperatingSystem.IsWindows())
        {
            var m = Mac.Format(mac, '-').ToLowerInvariant();
            var idx = network.InterfaceIndex;
            return $$"""
                $ErrorActionPreference = 'Continue'
                netsh interface ipv4 add neighbors interface={{idx}} address={{ip}} neighbor={{m}} store=active
                if ($LASTEXITCODE -ne 0) { exit 10 }
                $c = New-Object System.Net.Sockets.TcpClient
                try { $null = $c.ConnectAsync('{{ip}}', 1).Wait(3000) } catch { } finally { $c.Dispose() }
                netsh interface ipv4 delete neighbors interface={{idx}} address={{ip}} | Out-Null
                exit 0
                """;
        }
        var colon = Mac.Format(mac, ':').ToLowerInvariant();
        var iface = ShellQuote(network.InterfaceName);
        var (add, del) = OperatingSystem.IsMacOS()
            ? ($"arp -s {ip} {colon} ifscope {iface}", $"arp -d {ip} ifscope {iface}")
            : ($"ip neigh replace {ip} lladdr {colon} dev {iface} nud permanent", $"ip neigh del {ip} dev {iface}");
        return $"""
            {add} || exit 10
            ( exec 3<>/dev/tcp/{ip}/1 ) 2>/dev/null & p=$!
            for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do kill -0 $p 2>/dev/null || break; sleep 0.2; done
            kill $p 2>/dev/null
            {del} 2>/dev/null
            exit 0
            """;
    }

    /// <summary>
    /// Assigns <paramref name="ip"/> to the device with <paramref name="mac"/> and checks
    /// that it answers there afterwards.
    /// </summary>
    public static async Task<AssignResult> AssignAsync(PhysicalAddress mac, IPAddress ip, LocalNetwork network,
        Elevation elevation, IProgress<AssignStep>? progress = null, bool force = false, int port = Protocol.Port,
        CancellationToken ct = default)
    {
        if (!network.Contains(ip))
            return new(AssignStatus.NotInNetwork, ip);
        var netAddr = IPv4.FromUInt(IPv4.ToUInt(network.Address) & IPv4.ToUInt(network.Mask));
        if (ip.Equals(network.Address) || ip.Equals(network.Broadcast) || ip.Equals(netAddr))
            return new(AssignStatus.UnusableAddress, ip);

        progress?.Report(AssignStep.Checking);
        var already = await Ask(ip, mac, port, TimeSpan.FromSeconds(1), ct);
        if (already != null)
            return new(AssignStatus.AlreadyThere, ip, Device: already);
        if (!force && (network.Gateways.Contains(ip) || await Answers(ip)))
            return new(AssignStatus.InUse, ip);

        progress?.Report(AssignStep.Assigning);
        var (status, detail) = await RunScript(Script(mac, ip, network), elevation, ct);
        if (status != AssignStatus.Done) return new(status, ip, detail);

        progress?.Report(AssignStep.Verifying);
        for (var i = 0; i < 4 && !ct.IsCancellationRequested; i++)
        {
            var d = await Ask(ip, mac, port, TimeSpan.FromSeconds(1.5), ct);
            if (d != null) return new(AssignStatus.Done, ip, Device: d);
        }
        return new(AssignStatus.NoAnswer, ip);
    }

    private static async Task<DiscoveredDevice?> Ask(IPAddress ip, PhysicalAddress mac, int port, TimeSpan timeout, CancellationToken ct)
    {
        var o = new ScanOptions { Broadcast = false, Targets = [ip], Timeout = timeout, Port = port, StopAtFirst = true,
            MacFilter = Convert.ToHexString(mac.GetAddressBytes()) };
        await foreach (var d in Scanner.ScanAsync(o, ct)) return d;
        return null;
    }

    private static async Task<bool> Answers(IPAddress ip)
    {
        try
        {
            using var p = new Ping();
            return (await p.SendPingAsync(ip, 800)).Status == IPStatus.Success;
        }
        catch (Exception) { return false; } // no ping permission etc.: do not block the assignment
    }

    private static async Task<(AssignStatus, string)> RunScript(string script, Elevation elevation, CancellationToken ct)
    {
        ProcessStartInfo psi;
        var captured = true;
        if (OperatingSystem.IsWindows())
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            psi = new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded },
                CreateNoWindow = true,
            };
            if (!IsElevated && elevation != Elevation.None)
            {
                // UAC: only possible through the shell, so no output to read, just the exit code
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                captured = false;
            }
        }
        else if (IsElevated || elevation == Elevation.None)
            psi = Bash(script, null);
        else if (elevation == Elevation.Terminal)
            psi = Bash(script, "sudo");
        else if (OperatingSystem.IsMacOS())
        {
            var esc = script.Replace("\\", "\\\\").Replace("\"", "\\\"");
            psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                ArgumentList = { "-e", $"do shell script \"/bin/bash -c \" & quoted form of \"{esc}\" with administrator privileges" },
            };
        }
        else
            psi = Bash(script, "pkexec");

        if (captured)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            if (elevation == Elevation.Terminal && !OperatingSystem.IsWindows())
                psi.RedirectStandardInput = false; // sudo asks for the password on the terminal
        }

        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        { return (AssignStatus.Cancelled, ""); }
        catch (Exception e) { return (AssignStatus.Failed, $"{psi.FileName}: {e.Message}"); }
        if (p == null) return (AssignStatus.Failed, psi.FileName);

        using (p)
        {
            var err = captured ? p.StandardError.ReadToEndAsync(ct) : Task.FromResult("");
            var outp = captured ? p.StandardOutput.ReadToEndAsync(ct) : Task.FromResult("");
            await p.WaitForExitAsync(ct);
            var text = ((await err) + (await outp)).Trim();
            return p.ExitCode switch
            {
                0 => (AssignStatus.Done, ""),
                10 => (AssignStatus.ArpFailed, text),
                126 or 127 when elevation == Elevation.Graphical && OperatingSystem.IsLinux() => (AssignStatus.Cancelled, ""),
                1 when elevation == Elevation.Graphical && OperatingSystem.IsMacOS() && text.Contains("-128") => (AssignStatus.Cancelled, ""),
                _ => (AssignStatus.Failed, $"exit code {p.ExitCode}" + (text.Length > 0 ? $": {text}" : "")),
            };
        }
    }

    private static ProcessStartInfo Bash(string script, string? via)
    {
        var psi = new ProcessStartInfo(via ?? "/bin/bash");
        if (via != null) psi.ArgumentList.Add("/bin/bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        return psi;
    }

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
