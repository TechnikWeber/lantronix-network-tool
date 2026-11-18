using System.Globalization;
using Lantronix.Discovery;

namespace LantronixTool;

/// <summary>
/// All texts of the window, in German and English. German when the system language is
/// German, English otherwise; LANTRONIX_LANG=de|en overrides (for screenshots).
/// </summary>
public static class T
{
    public static readonly bool De = (Environment.GetEnvironmentVariable("LANTRONIX_LANG")
        ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "de";

    private static string S(string de, string en) => De ? de : en;

    public static string Search => S("Suchen", "Search");
    public static string Cancel => S("Abbrechen", "Cancel");
    public static string MacFilter => S("MAC-Filter, z. B. 2F-1A", "MAC filter, e.g. 2F-1A");
    public static string MacFilterTip => S(
        "Nur Geräte, deren MAC diese Ziffern enthält. Schreibweise egal: 2F-1A, 2f1a, BE:2F:1A …",
        "Only devices whose MAC contains these digits. Any notation: 2F-1A, 2f1a, BE:2F:1A …");
    public static string Quick => S("Schnell (3 s)", "Quick (3 s)");
    public static string Thorough => S("Gründlich (10 s)", "Thorough (10 s)");
    public static string Long => S("Lang (30 s)", "Long (30 s)");
    public static string More => S("Weitere Adressen direkt fragen", "Also ask addresses directly");
    public static string TargetsHint => S("z. B. 192.168.1.0/24 oder 10.0.0.10-50", "e.g. 192.168.1.0/24 or 10.0.0.10-50");
    public static string TargetsTip => S(
        "Fragt diese Adressen einzeln (Unicast). Hilft über Router hinweg und wenn eine Firewall " +
        "die Antworten auf den Broadcast verwirft.",
        "Asks these addresses one by one (unicast). Works across routers and when a firewall drops " +
        "the replies to the broadcast.");
    public static string Broadcast => S("Broadcast ins lokale Netz", "Broadcast to the local network");

    public static string ColIp => S("IP-Adresse", "IP address");
    public static string ColMac => S("MAC-Adresse", "MAC address");
    public static string ColNet => S("Netzwerk", "Network");

    public static string OpenWeb => S("Weboberfläche öffnen", "Open web interface");
    public static string AssignIp => S("IP zuweisen …", "Assign IP …");
    public static string Copy => S("Kopieren", "Copy");
    public static string CopyIp => S("IP-Adresse kopieren", "Copy IP address");
    public static string CopyMac => S("MAC-Adresse kopieren", "Copy MAC address");
    public static string CopyTelnet => S("Telnet-Befehl kopieren (Setup, Port 9999)", "Copy telnet command (setup, port 9999)");
    public static string CopyAll => S("Liste kopieren", "Copy list");
    public static string SaveCsv => S("Als CSV speichern …", "Save as CSV …");

    public static string Ready => S("Bereit. „Suchen“ fragt alle Lantronix-Geräte im lokalen Netz.",
        "Ready. “Search” asks every Lantronix device on the local network.");
    public static string EmptyStart => S("Noch keine Suche.", "No search yet.");
    public static string Searching(double s) => S($"Suche läuft ({s:0} s) …", $"Searching ({s:0} s) …");
    public static string Found(int n, double s) => n switch
    {
        0 => S($"Kein Gerät gefunden ({s:0.0} s).", $"No device found ({s:0.0} s)."),
        1 => S($"1 Gerät gefunden ({s:0.0} s).", $"1 device found ({s:0.0} s)."),
        _ => S($"{n} Geräte gefunden ({s:0.0} s).", $"{n} devices found ({s:0.0} s)."),
    };
    public static string FoundSoFar(int n) => S($"Suche läuft … {n} gefunden", $"Searching … {n} found");
    public static string Cancelled => S("Suche abgebrochen.", "Search cancelled.");
    public static string NothingFound => S(
        "Kein Gerät gefunden. Ist das Gerät eingeschaltet und im selben Netz (Kabel, Switch, kein Gast-WLAN)?",
        "No device found. Is the device powered and on the same network (cable, switch, no guest Wi-Fi)?");
    public static string FirewallHint => S(
        "Unter Linux verwirft eine Firewall oft die Antworten auf den Broadcast. Abhilfe bis zum Neustart:\n" +
        "  sudo firewall-cmd --add-source-port=30718/udp\n" +
        "oder unten „Weitere Adressen direkt fragen“ nutzen (z. B. 192.168.1.0/24).",
        "On Linux a firewall often drops the replies to the broadcast. Until the next reboot:\n" +
        "  sudo firewall-cmd --add-source-port=30718/udp\n" +
        "or use “Also ask addresses directly” below (e.g. 192.168.1.0/24).");
    public static string BadFilter => S("Der MAC-Filter darf nur Hex-Ziffern (0–9, A–F) und Trennzeichen enthalten.",
        "The MAC filter may only contain hex digits (0–9, A–F) and separators.");
    public static string BadTargets(string e) => S($"Adressen: {e}", $"Addresses: {e}");
    public static string NoQuery => S("Weder Broadcast noch Adressen – es gäbe niemanden zu fragen.",
        "Neither broadcast nor addresses — nobody to ask.");
    public static string Copied => S("In die Zwischenablage kopiert.", "Copied to the clipboard.");
    public static string Saved(string p) => S($"Gespeichert: {p}", $"Saved: {p}");

    public static string NetLoopback => S("Loopback (Demo)", "loopback (demo)");
    public static string NoAddress => S("hat noch keine IP – „IP zuweisen“", "no IP address yet — “Assign IP”");
    public static string OtherSubnet => S("anderes Subnetz – von hier nicht erreichbar", "other subnet — not reachable from here");
    public static string NetText(DiscoveredDevice d) =>
        d.HasNoAddress ? NoAddress
        : d.Network != null ? d.Network.InterfaceName
        : System.Net.IPAddress.IsLoopback(d.Address) ? NetLoopback
        : OtherSubnet;

    // assign window
    public static string AssignTitle => S("IP-Adresse zuweisen", "Assign IP address");
    public static string AssignIntro => S(
        "Gibt einem Gerät vorübergehend eine IP-Adresse, auch wenn es gerade keine oder eine unpassende hat. " +
        "Dazu legt das Programm einen ARP-Eintrag (IP → MAC) an und klopft auf Port 1; das Gerät übernimmt " +
        "die Adresse. Dafür sind Administratorrechte nötig, es erscheint eine Passwortabfrage.\n\n" +
        "Die Adresse gilt bis zum nächsten Neustart des Geräts. Dauerhaft setzt man sie danach in der " +
        "Weboberfläche oder per Telnet auf Port 9999.\n\n" +
        "Funktioniert bei den meisten älteren Geräten (XPort, UDS, EDS, MSS, WiPort …); manche nehmen es nur an, " +
        "solange sie keine IP haben. Geräte mit Evolution OS (XPort EDGE, EDS3000, PremierWave) eventuell gar nicht.",
        "Gives a device a temporary IP address, even when it has none or one that does not fit. The program " +
        "adds an ARP entry (IP → MAC) and knocks on port 1; the device takes over the address. This needs " +
        "administrator rights, you will be asked for a password.\n\n" +
        "The address lasts until the device restarts. Set it permanently afterwards in the web interface or " +
        "via telnet on port 9999.\n\n" +
        "Works with most older devices (XPort, UDS, EDS, MSS, WiPort …); some only accept it while they have " +
        "no IP address. Evolution OS devices (XPort EDGE, EDS3000, PremierWave) possibly not at all.");
    public static string AssignMac => S("MAC-Adresse (steht auf dem Aufkleber)", "MAC address (on the label)");
    public static string AssignIpLabel => S("Neue IP-Adresse", "New IP address");
    public static string AssignNet => S("Über Netzwerk", "Via network");
    public static string AssignCommands => S("Befehle, die ausgeführt werden", "Commands that will run");
    public static string AssignGo => S("Zuweisen", "Assign");
    public static string Close => S("Schließen", "Close");
    public static string AssignBadMac => S("Bitte die vollständige MAC-Adresse eingeben (12 Hex-Ziffern).",
        "Please enter the complete MAC address (12 hex digits).");
    public static string AssignBadIp => S("Bitte eine IPv4-Adresse eingeben.", "Please enter an IPv4 address.");
    public static string AssignNoNet => S("Diese Adresse liegt in keinem Netz dieses Computers. Die neue IP muss " +
        "zum Netz des Computers passen.", "This address is in none of this computer's networks. The new IP must " +
        "fit the computer's network.");

    public static string Step(AssignStep s, string ip) => s switch
    {
        AssignStep.Checking => S($"Prüfe, ob {ip} frei ist …", $"Checking whether {ip} is free …"),
        AssignStep.Assigning => S("Setze ARP-Eintrag und klopfe auf Port 1 …", "Setting the ARP entry and knocking on port 1 …"),
        _ => S($"Warte auf das Gerät unter {ip} …", $"Waiting for the device at {ip} …"),
    };

    public static string Result(AssignResult r)
    {
        if (!De) return r.Message;
        var ip = r.Ip.ToString();
        var text = r.Status switch
        {
            AssignStatus.Done => $"Fertig: Das Gerät antwortet jetzt unter {ip} (bis zum Neustart).",
            AssignStatus.AlreadyThere => $"Das Gerät antwortet bereits unter {ip}.",
            AssignStatus.NotInNetwork => $"{ip} liegt nicht im gewählten Netz.",
            AssignStatus.UnusableAddress => $"{ip} geht nicht (eigene, Netz- oder Broadcast-Adresse).",
            AssignStatus.InUse => $"{ip} ist schon belegt (Router oder antwortet auf Ping). Bitte eine andere Adresse wählen.",
            AssignStatus.ArpFailed => "Der ARP-Eintrag ließ sich nicht setzen (Administratorrechte?)",
            AssignStatus.Cancelled => "Abgebrochen (keine Administratorrechte erteilt).",
            AssignStatus.NoAnswer => $"Das Gerät antwortet nicht unter {ip}. MAC-Adresse prüfen; manche Modelle nehmen " +
                                     "die ARP-Methode nur an, solange sie keine IP haben, Evolution-OS-Geräte gar nicht.",
            _ => "Fehlgeschlagen.",
        };
        return text + (r.Detail.Length > 0 ? $" ({r.Detail})" : "");
    }
}
