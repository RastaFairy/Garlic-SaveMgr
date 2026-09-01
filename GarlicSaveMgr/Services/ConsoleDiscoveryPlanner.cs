using System.Buffers.Binary;
using System.Net;

namespace GarlicSaveMgr.Services;

public static class ConsoleDiscoveryPlanner
{
    public sealed record NetworkSnapshot(
        IPAddress Address,
        IPAddress Mask,
        IReadOnlyList<IPAddress> Gateways);

    public static IReadOnlyList<string> BuildQuickCandidates(IEnumerable<NetworkSnapshot> networks)
    {
        var result = new List<string>(512);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var network in networks)
        {
            Add(network.Address);
            foreach (var gateway in network.Gateways)
                Add(gateway);

            var address = network.Address.GetAddressBytes();
            var mask = network.Mask.GetAddressBytes();
            var prefixLength = PrefixLength(mask);

            if (prefixLength >= 24)
            {
                var networkBytes = And(address, mask);
                var hostBits = 32 - prefixLength;
                var hostCount = (1 << hostBits) - 2;
                for (var host = 1; host <= hostCount; host++)
                    Add(WithHost(networkBytes, host));
            }
            else
            {
                var local24 = new[] { address[0], address[1], address[2], (byte)0 };
                for (var host = 1; host <= 254; host++)
                {
                    local24[3] = (byte)host;
                    Add(new IPAddress(local24));
                }
            }
        }

        return result;

        void Add(IPAddress address)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.None))
                return;

            var value = address.ToString();
            if (seen.Add(value)) result.Add(value);
        }
    }

    public static IReadOnlyList<string> BuildExpandedCandidates(
        IEnumerable<NetworkSnapshot> networks,
        IEnumerable<string> alreadyScanned)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(alreadyScanned, StringComparer.OrdinalIgnoreCase);

        foreach (var network in networks)
        {
            var address = network.Address.GetAddressBytes();
            var mask = network.Mask.GetAddressBytes();
            var prefixLength = PrefixLength(mask);
            if (prefixLength < 16 || prefixLength >= 31)
                continue;

            var networkBytes = And(address, mask);
            var hostBits = 32 - prefixLength;
            var hostCount = (1 << hostBits) - 2;
            for (var host = 1; host <= hostCount; host++)
            {
                var candidate = WithHost(networkBytes, host).ToString();
                if (seen.Add(candidate)) result.Add(candidate);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> BuildWideCandidates(
        IEnumerable<NetworkSnapshot> networks,
        IEnumerable<string> alreadyScanned)
    {
        var result = new List<string>(65534);
        var seen = new HashSet<string>(alreadyScanned, StringComparer.OrdinalIgnoreCase);

        // The explicit wide fallback is intentionally limited to 192.168.0.0/16.
        // This catches PS5 consoles on another 192.168.x subnet reachable through
        // the local router without turning autodetection into a potentially huge
        // scan of every RFC1918 address.
        var has192168Interface = networks.Any(n =>
        {
            var bytes = n.Address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 192 && bytes[1] == 168;
        });

        if (!has192168Interface)
            return result;

        var networkBytes = new byte[] { 192, 168, 0, 0 };
        for (var host = 1; host < 65535; host++)
        {
            var candidate = WithHost(networkBytes, host).ToString();
            if (seen.Add(candidate)) result.Add(candidate);
        }

        return result;
    }

    private static byte[] And(byte[] address, byte[] mask)
    {
        var result = new byte[4];
        for (var i = 0; i < 4; i++) result[i] = (byte)(address[i] & mask[i]);
        return result;
    }

    private static IPAddress WithHost(byte[] networkBytes, int host)
    {
        var networkValue = BinaryPrimitives.ReadUInt32BigEndian(networkBytes);
        var candidate = networkValue | (uint)host;
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, candidate);
        return new IPAddress(bytes);
    }

    private static int PrefixLength(byte[] mask)
    {
        var prefix = 0;
        foreach (var octet in mask)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                if ((octet & (1 << bit)) == 0) return prefix;
                prefix++;
            }
        }
        return prefix;
    }
}
