using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lantronix.Discovery;

namespace LantronixTool;

/// <summary>One line in the device list.</summary>
public sealed class DeviceRow(DiscoveredDevice d)
{
    public DiscoveredDevice Device { get; } = d;
    public string Ip => Device.Address.ToString();
    public string Mac => Device.MacText;
    public string Network => T.NetText(Device);
    public bool Unusual => !Device.Reachable || Device.HasNoAddress;
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DeviceRow> _rows = [];
    private CancellationTokenSource? _scan;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Lantronix Network Tool {App.Version}";
        List.ItemsSource = _rows;
        StatusText.Text = T.Ready;
        EmptyText.Text = T.EmptyStart;
        RowMenu.Opening += (_, e) => e.Cancel = Selected == null;
    }

    private DeviceRow? Selected => List.SelectedItem as DeviceRow;

    private void ShowEmpty(string? text)
    {
        EmptyText.Text = text ?? "";
        EmptyText.IsVisible = text != null;
    }

    private async void OnSearch(object? sender, RoutedEventArgs e) => await SearchAsync();

    /// <summary>Runs a search with the settings in the window (also used by the screenshot tool).</summary>
    public async Task SearchAsync()
    {
        if (_scan != null) return;

        var filter = FilterBox.Text?.Trim();
        if (!Mac.IsValidFilter(filter))
        {
            StatusText.Text = T.BadFilter;
            return;
        }
        IReadOnlyList<IPAddress> targets = [];
        if (!string.IsNullOrWhiteSpace(TargetsBox.Text) &&
            !AddressRange.TryParse(TargetsBox.Text, out targets, out var err))
        {
            StatusText.Text = T.BadTargets(err!);
            return;
        }
        var broadcast = BroadcastBox.IsChecked == true;
        if (!broadcast && targets.Count == 0)
        {
            StatusText.Text = T.NoQuery;
            return;
        }

        var seconds = DurationBox.SelectedIndex switch { 1 => 10, 2 => 30, _ => 3 };
        var options = new ScanOptions
        {
            Timeout = TimeSpan.FromSeconds(seconds),
            MacFilter = filter,
            Targets = targets,
            Broadcast = broadcast,
        };

        _rows.Clear();
        ShowEmpty(null);
        SetBusy(true);
        StatusText.Text = T.Searching(seconds);
        _scan = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cancelled = false;
        try
        {
            await foreach (var d in Scanner.ScanAsync(options, _scan.Token))
                Insert(new DeviceRow(d));
        }
        catch (OperationCanceledException) { cancelled = true; }
        finally
        {
            _scan.Dispose();
            _scan = null;
            SetBusy(false);
        }

        StatusText.Text = cancelled ? T.Cancelled : T.Found(_rows.Count, sw.Elapsed.TotalSeconds);
        if (_rows.Count == 0)
        {
            var hint = Firewall.Hint();
            ShowEmpty(hint == null ? T.NothingFound
                : T.NothingFound + "\n\n" + (hint.Contains("firewall-cmd") ? T.FirewallHint : hint));
        }
        else if (List.SelectedItem == null) List.SelectedIndex = 0;
        UpdateButtons();
    }

    // keep the list sorted by IP address while results come in
    private void Insert(DeviceRow row)
    {
        var key = IPv4.ToUInt(row.Device.Address);
        var i = 0;
        while (i < _rows.Count && IPv4.ToUInt(_rows[i].Device.Address) <= key) i++;
        _rows.Insert(i, row);
        if (_scan != null) StatusText.Text = T.FoundSoFar(_rows.Count);
        UpdateButtons();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _scan?.Cancel();

    private void SetBusy(bool busy)
    {
        Busy.IsVisible = busy;
        SearchButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private void UpdateButtons()
    {
        OpenButton.IsEnabled = Selected is { Device.Reachable: true, Device.HasNoAddress: false };
        CopyButton.IsEnabled = CsvButton.IsEnabled = _rows.Count > 0;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => OnOpenWeb(sender, e);

    private async void OnOpenWeb(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || row.Device.HasNoAddress) return;
        await Launcher.LaunchUriAsync(new Uri($"http://{row.Ip}/"));
    }

    private async void OnAssign(object? sender, RoutedEventArgs e)
    {
        var dialog = new AssignWindow(Selected?.Device);
        await dialog.ShowDialog(this);
        if (dialog.Result?.Device is { } d)
        {
            var existing = _rows.FirstOrDefault(r => r.Device.Mac.Equals(d.Mac));
            if (existing != null) _rows.Remove(existing);
            ShowEmpty(null);
            Insert(new DeviceRow(d));
            List.SelectedItem = _rows.First(r => r.Device == d);
            StatusText.Text = T.Result(dialog.Result);
        }
    }

    private async Task ToClipboard(string text)
    {
        if (Clipboard is { } c)
        {
            await c.SetTextAsync(text);
            StatusText.Text = T.Copied;
        }
    }

    private async void OnCopyIp(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } r) await ToClipboard(r.Ip);
    }

    private async void OnCopyMac(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } r) await ToClipboard(r.Mac);
    }

    private async void OnCopyTelnet(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } r) await ToClipboard($"telnet {r.Ip} 9999");
    }

    private async void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var r in _rows) sb.Append(r.Ip).Append('\t').Append(r.Mac).Append('\t').AppendLine(r.Network);
        await ToClipboard(sb.ToString());
    }

    private async void OnSaveCsv(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T.SaveCsv.TrimEnd(' ', '…'),
            SuggestedFileName = $"lantronix-{DateTime.Now:yyyy-MM-dd-HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file == null) return;
        var sb = new StringBuilder("ip,mac,interface,reachable,reply\n");
        foreach (var r in _rows)
        {
            var d = r.Device;
            sb.Append($"{d.Address},{d.MacText},\"{d.Network?.InterfaceName.Replace("\"", "\"\"")}\",")
              .Append(d.Reachable ? "true" : "false").Append(',').Append(Convert.ToHexString(d.Reply)).Append('\n');
        }
        await using (var stream = await file.OpenWriteAsync())
        await using (var w = new StreamWriter(stream, new UTF8Encoding(false)))
            await w.WriteAsync(sb.ToString());
        StatusText.Text = T.Saved(file.Path.IsFile ? file.Path.LocalPath : file.Name);
    }
}
