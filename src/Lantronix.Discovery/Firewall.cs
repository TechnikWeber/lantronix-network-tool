namespace Lantronix.Discovery;

public static class Firewall
{
    /// <summary>
    /// A hint for Linux when nothing was found: the reply comes from the device's own
    /// address, not from the broadcast address the query went to, so a stateful firewall
    /// (firewalld, ufw) does not recognise it as an answer and may drop it. Null elsewhere
    /// or when no such firewall is active.
    /// </summary>
    public static string? Hint()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            if (Directory.Exists("/run/firewalld") && IsActive("firewalld"))
                return "firewalld may drop the replies to a broadcast. Allow them until the next reboot with\n" +
                       "  sudo firewall-cmd --add-source-port=30718/udp\n" +
                       "or ask the devices directly (unicast scan, e.g. 192.168.1.0/24).";
            if (File.Exists("/etc/ufw/ufw.conf") && File.ReadAllText("/etc/ufw/ufw.conf").Contains("ENABLED=yes"))
                return "ufw may drop the replies to a broadcast. Allow them with\n" +
                       "  sudo ufw allow proto udp from any port 30718\n" +
                       "or ask the devices directly (unicast scan, e.g. 192.168.1.0/24).";
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static bool IsActive(string unit)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("systemctl") { ArgumentList = { "is-active", "--quiet", unit } };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return true;
            if (!p.WaitForExit(1500)) { p.Kill(); return true; }
            return p.ExitCode == 0;
        }
        catch (Exception) { return true; } // no systemd: the directory is all we know
    }
}
