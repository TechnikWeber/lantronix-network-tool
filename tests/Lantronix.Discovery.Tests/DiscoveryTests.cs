using System.Net;
using System.Net.Sockets;
using Lantronix.Discovery;
using Xunit;

namespace Lantronix.Discovery.Tests;

public class ProtocolTests
{
    private static byte[] Reply(string mac) =>
        [0, 0, 0, 0xF7, .. new byte[20], .. Convert.FromHexString(mac)];

    [Fact]
    public void ParsesTheMacFromBytes24To29()
        => Assert.Equal("00-80-A3-BE-2F-1A", Mac.Format(Protocol.ParseReply(Reply("0080A3BE2F1A"))!));

    [Fact]
    public void RejectsShortOrForeignPackets()
    {
        Assert.Null(Protocol.ParseReply(Reply("0080A3BE2F1A")[..29]));
        var setup = Reply("0080A3BE2F1A");
        setup[3] = 0xF9;
        Assert.Null(Protocol.ParseReply(setup));
        Assert.Null(Protocol.ParseReply([0, 0, 0, 0xF6]));
    }
}

public class MacTests
{
    private static readonly System.Net.NetworkInformation.PhysicalAddress M =
        new(Convert.FromHexString("0080A3BE2F1A"));

    [Theory]
    [InlineData("2F-1A")]
    [InlineData("2f1a")]
    [InlineData("be:2f:1a")]
    [InlineData("00-80-A3-BE-2F-1A")]
    [InlineData("0080.a3be.2f1a")]
    [InlineData("")]
    [InlineData(null)]
    public void FilterMatchesInAnyNotation(string? filter) => Assert.True(Mac.Matches(M, filter));

    [Theory]
    [InlineData("2F-1B")]
    [InlineData("xyz")]
    [InlineData("1A-2F")]
    public void FilterRejects(string filter) => Assert.False(Mac.Matches(M, filter));

    [Fact]
    public void ParsesFullMacs()
    {
        Assert.True(Mac.TryParse("00:80:a3:be:2f:1a", out var m));
        Assert.Equal(M, m);
        Assert.False(Mac.TryParse("00-80-A3-BE-2F", out _));
        Assert.False(Mac.TryParse("00-80-A3-BE-2F-1G", out _));
    }
}

public class AddressRangeTests
{
    [Fact]
    public void Network24WithoutNetworkAndBroadcast()
    {
        Assert.True(AddressRange.TryParse("192.168.1.0/24", out var a, out _));
        Assert.Equal(254, a.Count);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), a[0]);
        Assert.Equal(IPAddress.Parse("192.168.1.254"), a[^1]);
    }

    [Fact]
    public void ShortAndLongRangesAndLists()
    {
        Assert.True(AddressRange.TryParse("10.0.0.10-12, 10.0.0.20-10.0.0.21 10.0.0.10", out var a, out _));
        Assert.Equal(["10.0.0.10", "10.0.0.11", "10.0.0.12", "10.0.0.20", "10.0.0.21"], a.Select(x => x.ToString()));
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.0.0.1-10.1.0.1")]
    [InlineData("10.1")]
    [InlineData("nope")]
    [InlineData("10.0.0.0/33")]
    public void Refuses(string text) => Assert.False(AddressRange.TryParse(text, out _, out _));
}

/// <summary>A simulated device on 127.0.0.1 and a random port, like demo/fake_devices.py.</summary>
public sealed class FakeDevice : IDisposable
{
    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _cts = new();
    public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    public int Queries;

    public FakeDevice(string mac, int replies = 1)
    {
        byte[] reply = [0, 0, 0, 0xF7, .. new byte[20], .. Convert.FromHexString(mac)];
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var r = await _udp.ReceiveAsync(_cts.Token);
                    if (r.Buffer is not [0, 0, 0, 0xF6]) continue;
                    Interlocked.Increment(ref Queries);
                    for (var i = 0; i < replies; i++) await _udp.SendAsync(reply, r.RemoteEndPoint, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
    }
}

public class ScannerTests
{
    private static ScanOptions Direct(int port, double timeout = 0.5) => new()
    {
        Broadcast = false, Targets = [IPAddress.Loopback], Port = port, Timeout = TimeSpan.FromSeconds(timeout),
    };

    [Fact]
    public async Task FindsTheDeviceOnceAndStopsAfterTheTimeout()
    {
        using var dev = new FakeDevice("0080A3BE2F1A", replies: 5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var found = await Scanner.ScanAllAsync(Direct(dev.Port), TestContext.Current.CancellationToken);
        sw.Stop();

        var d = Assert.Single(found);
        Assert.Equal(IPAddress.Loopback, d.Address);
        Assert.Equal("00-80-A3-BE-2F-1A", d.MacText);
        Assert.True(d.Reachable);
        Assert.InRange(sw.Elapsed.TotalSeconds, 0.4, 3);
        Assert.Equal(1, dev.Queries); // no broadcast storm: one query per round, one round without broadcast
    }

    [Fact]
    public async Task MacFilterAndStopAtFirst()
    {
        using var dev = new FakeDevice("0080A3BE2F1A");
        var miss = Direct(dev.Port);
        miss.MacFilter = "2f-1b";
        Assert.Empty(await Scanner.ScanAllAsync(miss, TestContext.Current.CancellationToken));

        var o = Direct(dev.Port, timeout: 5);
        o.MacFilter = "2F1A";
        o.StopAtFirst = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Single(await Scanner.ScanAllAsync(o, TestContext.Current.CancellationToken));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), "StopAtFirst should not wait for the timeout");
    }

    [Fact]
    public async Task CancellationEndsTheScan()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var o = Direct(9, timeout: 30);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await Scanner.ScanAllAsync(o, cts.Token); }
        catch (OperationCanceledException) { }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }
}

public class IpAssignerTests
{
    private static readonly LocalNetwork Net = new("eth 0", "x", 3, IPAddress.Parse("192.168.1.17"), IPAddress.Parse("255.255.255.0"));
    private static readonly System.Net.NetworkInformation.PhysicalAddress M = new(Convert.FromHexString("0080A3BE2F1A"));

    [Fact]
    public void ScriptIsValidShell()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = IpAssigner.Script(M, IPAddress.Parse("192.168.1.50"), Net);
        Assert.Contains("00:80:a3:be:2f:1a", script);
        Assert.Contains("'eth 0'", script); // interface name quoted
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash") { ArgumentList = { "-n", "-c", script }, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }

    [Theory]
    [InlineData("192.168.2.50", AssignStatus.NotInNetwork)]
    [InlineData("192.168.1.17", AssignStatus.UnusableAddress)]
    [InlineData("192.168.1.255", AssignStatus.UnusableAddress)]
    [InlineData("192.168.1.0", AssignStatus.UnusableAddress)]
    public async Task RefusesBadAddressesBeforeDoingAnything(string ip, AssignStatus expected)
    {
        var r = await IpAssigner.AssignAsync(M, IPAddress.Parse(ip), Net, Elevation.None,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(expected, r.Status);
        Assert.False(r.Success);
    }
}
