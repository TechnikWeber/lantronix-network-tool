using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lantronix.Discovery;

namespace LantronixTool;

public partial class AssignWindow : Window
{
    private readonly IReadOnlyList<LocalNetwork> _networks = LocalNetworks.Get();
    private bool _busy;

    /// <summary>The outcome of the last attempt, null when none was made.</summary>
    public AssignResult? Result { get; private set; }

    public AssignWindow() : this(null) { }

    public AssignWindow(DiscoveredDevice? device)
    {
        InitializeComponent();
        NetBox.ItemsSource = _networks.Select(n => n.ToString()).ToList();
        if (_networks.Count > 0) NetBox.SelectedIndex = 0;
        if (device != null)
        {
            MacBox.Text = device.MacText;
            if (device.Network != null && !device.HasNoAddress)
            {
                IpBox.Text = device.Address.ToString();
                NetBox.SelectedIndex = IndexOf(device.Network);
            }
        }
        IpBox.PlaceholderText = _networks.Count > 0 ? Suggest(_networks[0]) : "192.168.1.50";
        Opened += (_, _) => (string.IsNullOrEmpty(MacBox.Text) ? MacBox : IpBox).Focus();
        Validate();
    }

    private int IndexOf(LocalNetwork n)
    {
        // compare by value: the scan has its own LocalNetwork instances
        for (var i = 0; i < _networks.Count; i++)
            if (_networks[i].InterfaceId == n.InterfaceId && _networks[i].Address.Equals(n.Address)) return i;
        return -1;
    }

    // e.g. 192.168.1.17/24 -> "192.168.1.…"
    private static string Suggest(LocalNetwork n)
    {
        var parts = n.Address.ToString().Split('.');
        return n.PrefixLength >= 24 ? $"{parts[0]}.{parts[1]}.{parts[2]}.…"
            : n.PrefixLength >= 16 ? $"{parts[0]}.{parts[1]}.…"
            : $"{parts[0]}.…";
    }

    private void OnNetChanged(object? sender, SelectionChangedEventArgs e) => Validate();

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        // pick the network that fits the typed address
        if (sender == IpBox && TryIp(out var ip) && LocalNetworks.Find(_networks, ip) is { } n)
        {
            var i = IndexOf(n);
            if (i >= 0 && NetBox.SelectedIndex != i) NetBox.SelectedIndex = i;
        }
        Validate();
    }

    private bool TryIp(out IPAddress ip)
        => IPAddress.TryParse(IpBox.Text?.Trim(), out ip!) && ip.AddressFamily == AddressFamily.InterNetwork
           && IpBox.Text!.Count(c => c == '.') == 3;

    private (PhysicalAddress Mac, IPAddress Ip, LocalNetwork Net)? Validate()
    {
        string? problem = null;
        PhysicalAddress mac = PhysicalAddress.None;
        IPAddress ip = IPAddress.None;
        LocalNetwork? net = NetBox.SelectedIndex >= 0 && NetBox.SelectedIndex < _networks.Count
            ? _networks[NetBox.SelectedIndex] : null;

        if (!Mac.TryParse(MacBox.Text ?? "", out mac)) problem = string.IsNullOrWhiteSpace(MacBox.Text) ? "" : T.AssignBadMac;
        else if (!TryIp(out ip)) problem = string.IsNullOrWhiteSpace(IpBox.Text) ? "" : T.AssignBadIp;
        else if (net == null || !net.Contains(ip)) problem = T.AssignNoNet;

        GoButton.IsEnabled = problem == null && !_busy;
        if (!_busy) StatusText.Text = problem ?? "";
        ScriptText.Text = problem == null ? IpAssigner.Script(mac, ip, net!) : "";
        return problem == null ? (mac, ip, net!) : null;
    }

    private async void OnGo(object? sender, RoutedEventArgs e)
    {
        if (Validate() is not { } input) return;
        _busy = true;
        GoButton.IsEnabled = MacBox.IsEnabled = IpBox.IsEnabled = NetBox.IsEnabled = false;
        var ipText = input.Ip.ToString();
        try
        {
            Result = await IpAssigner.AssignAsync(input.Mac, input.Ip, input.Net, Elevation.Graphical,
                new Progress<AssignStep>(s => StatusText.Text = T.Step(s, ipText)));
            StatusText.Text = T.Result(Result);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            MacBox.IsEnabled = IpBox.IsEnabled = NetBox.IsEnabled = true;
            GoButton.IsEnabled = Result?.Success != true;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
