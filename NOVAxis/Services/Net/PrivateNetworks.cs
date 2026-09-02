using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Net
{
    /// <summary>
    /// Tells an address on the public internet apart from one that leads back into the host's
    /// own network. Used to keep a link somebody pasted from turning the bot into a way of
    /// reaching things only the bot can reach.
    /// </summary>
    public static class PrivateNetworks
    {
        public static bool IsBlocked(IPAddress address)
        {
            if (address == null)
                return true;

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsBlockedV4(address.GetAddressBytes()),
                AddressFamily.InterNetworkV6 => IsBlockedV6(address),
                _ => true
            };
        }

        private static bool IsBlockedV4(byte[] o)
        {
            return o[0] switch
            {
                0 => true,                                              // this network
                10 => true,                                             // private
                127 => true,                                            // loopback
                169 when o[1] == 254 => true,                           // link local, and the cloud metadata address
                172 when o[1] >= 16 && o[1] <= 31 => true,              // private
                192 when o[1] == 168 => true,                           // private
                192 when o[1] == 0 && o[2] == 0 => true,                // IETF protocol assignments
                192 when o[1] == 0 && o[2] == 2 => true,                // documentation
                192 when o[1] == 88 && o[2] == 99 => true,              // 6to4 relay anycast
                198 when o[1] == 18 || o[1] == 19 => true,              // benchmarking
                198 when o[1] == 51 && o[2] == 100 => true,             // documentation
                203 when o[1] == 0 && o[2] == 113 => true,              // documentation
                100 when o[1] >= 64 && o[1] <= 127 => true,             // carrier grade nat
                >= 224 => true,                                         // multicast, reserved, broadcast
                _ => false
            };
        }

        private static bool IsBlockedV6(IPAddress address)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any))
                return true;

            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
                address.IsIPv6UniqueLocal || address.IsIPv6Multicast)
                return true;

            var o = address.GetAddressBytes();

            // 6to4 (2002::/16) and NAT64 (64:ff9b::/96) carry a v4 address inside, and a
            // blocked one stays blocked however it is wrapped
            if (o[0] == 0x20 && o[1] == 0x02)
                return IsBlockedV4([o[2], o[3], o[4], o[5]]);

            if (o[0] == 0x00 && o[1] == 0x64 && o[2] == 0xff && o[3] == 0x9b)
                return IsBlockedV4([o[12], o[13], o[14], o[15]]);

            return false;
        }

        /// <summary>
        /// Resolves a host and returns the addresses worth connecting to, or an empty list if
        /// any of them leads somewhere private. A name answering with both a public and a
        /// private address is the shape of a rebinding attempt, so the whole name is refused
        /// rather than the offending record quietly dropped.
        /// </summary>
        public static async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, bool allowPrivate = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                return [];

            if (IPAddress.TryParse(host, out var literal))
                return !allowPrivate && IsBlocked(literal) ? [] : [literal];

            IPAddress[] addresses;

            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
            catch (SocketException)
            {
                return [];
            }

            if (addresses.Length == 0 || (!allowPrivate && addresses.Any(IsBlocked)))
                return [];

            return addresses;
        }
    }
}
