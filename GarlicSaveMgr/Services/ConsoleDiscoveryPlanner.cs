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
                var hostCount = hostBits >= 31 ? 0 : (1 << hostBits) - 2;
                for (var host = 1; host <= hostCount; host++)
                {
                    var candidate = WithHost(networkBytes, prefixLength, host);
                    Add(candidate);
                }
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

            // A /15 or larger can mean millions of hosts. The quick /24 pass is
            // intentional for these oversized networks; full expansion is not
            // attempted to avoid an unusable discovery operation.
            if (prefixLength < 16 || prefixLength >= 31)
                continue;

            var networkBytes = And(address, mask);
            var hostBits = 32 - prefixLength;
            var hostCount = (1 << hostBits) - 2;
            for (var host = 1; host <= hostCount; host++)
            {
                var candidate = WithHost(networkBytes, prefixLength, host).ToString();
                if (seen.Add(candidate)) result.Add(candidate);
            }
        }

        return result;
    }

    private static byte[] And(byte[] address, byte[] mask)
    {
        var result = new byte[4];
        for (var i = 0; i < 4; i++) result[i] = (byte)(address[i] & mask[i]);
        return result;
    }

    private static IPAddress WithHost(byte[] networkBytes, int prefixLength, int host)
    {
        var bytes = (byte[])networkBytes.Clone();
        var hostBits = 32 - prefixLength;
        var value = (uint)host;
        for (var bit = 0; bit < hostBits; bit++)
        {
            var absoluteBit = 31 - bit;
            var byteIndex = absoluteBit / 8;
            var bitIndex = absoluteBit % 8;
            if ((value & (1u << bit)) != 0)
                bytes[byteIndex] |= (byte)(1 << bitIndex);
        }
        return new IPAddress(bytes);
    }

    private static int PrefixLength(byte[] mask)
    {
        var prefix = 0;
        foreach (var octet in mask)
        {
            var value = octet;
            for (var bit = 7; bit >= 0; bit--)
            {
                if ((value & (1 << bit)) == 0) return prefix;
                prefix++;
            }
        }
        return prefix;
    }
}
