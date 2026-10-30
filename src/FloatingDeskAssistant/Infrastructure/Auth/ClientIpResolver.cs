using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FloatingDeskAssistant.Application;

namespace FloatingDeskAssistant.Infrastructure.Auth;

public sealed class ClientIpResolver : IClientIpResolver
{
    public string? GetCurrentIpv4Address()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                    && ni.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .SelectMany(GetCandidates)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Address.ToString())
                .FirstOrDefault();
        }
        catch (NetworkInformationException) { return null; }
    }

    private static IEnumerable<(IPAddress Address, int Score)> GetCandidates(NetworkInterface network)
    {
        IPInterfaceProperties properties;
        try { properties = network.GetIPProperties(); }
        catch (NetworkInformationException) { yield break; }
        var score = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork) ? 50 : 0;
        if (network.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) score += 20;
        foreach (var item in properties.UnicastAddresses)
        {
            var address = item.Address;
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254) continue;
            yield return (address, score + (item.IsDnsEligible ? 10 : 0));
        }
    }
}
