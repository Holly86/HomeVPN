using System.Net;
using System.Net.Sockets;

namespace HomeVpn.Core;

public sealed class Cidr
{
    public IPAddress Network { get; }
    public int PrefixLength { get; }

    private Cidr(IPAddress network, int prefixLength)
    {
        Network = network;
        PrefixLength = prefixLength;
    }

    public static bool TryParse(string? value, out Cidr? cidr)
    {
        cidr = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address))
            return false;

        var maxBits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxBits;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maxBits))
            return false;

        var bytes = address.GetAddressBytes();
        ApplyMask(bytes, prefix);
        cidr = new Cidr(new IPAddress(bytes), prefix);
        return true;
    }

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != Network.AddressFamily)
            return false;

        var candidate = address.GetAddressBytes();
        var network = Network.GetAddressBytes();
        ApplyMask(candidate, PrefixLength);
        return candidate.SequenceEqual(network);
    }

    public override string ToString() => $"{Network}/{PrefixLength}";

    public static string FromAddressAndPrefix(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        ApplyMask(bytes, prefixLength);
        return $"{new IPAddress(bytes)}/{prefixLength}";
    }

    private static void ApplyMask(byte[] bytes, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        if (fullBytes < bytes.Length && remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            bytes[fullBytes] &= mask;
            fullBytes++;
        }

        for (var i = fullBytes; i < bytes.Length; i++)
            bytes[i] = 0;
    }
}
