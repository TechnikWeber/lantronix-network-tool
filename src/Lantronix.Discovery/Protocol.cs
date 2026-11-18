using System.Net.NetworkInformation;

namespace Lantronix.Discovery;

/// <summary>
/// The parts of the Lantronix discovery protocol (UDP 30718) this library uses.
/// </summary>
/// <remarks>
/// Query: the four bytes <c>00 00 00 F6</c>. Reply: <c>00 00 00 F7</c>, 30 bytes in total,
/// the device's MAC address in bytes 24..29. The meaning of bytes 4..23 is not publicly
/// documented, so they are passed on untouched as <see cref="DiscoveredDevice.Reply"/>.
/// </remarks>
public static class Protocol
{
    public const int Port = 30718;
    public const int ReplyLength = 30;
    public const int MacOffset = 24;

    /// <summary>The discovery query.</summary>
    public static ReadOnlySpan<byte> Query => [0x00, 0x00, 0x00, 0xF6];

    /// <summary>Reads the MAC address from a discovery reply; null when it is no valid reply.</summary>
    public static PhysicalAddress? ParseReply(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < ReplyLength) return null;
        if (packet[0] != 0 || packet[1] != 0 || packet[2] != 0 || packet[3] != 0xF7) return null;
        return new PhysicalAddress(packet.Slice(MacOffset, 6).ToArray());
    }
}
