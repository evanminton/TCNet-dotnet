using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;

namespace TCNet.Networking;

/// <summary>An IPv4 interface with its directed broadcast address.</summary>
public sealed record NetworkInterfaceInfo(string Name, IPAddress Address, IPAddress Mask, IPAddress Broadcast)
{
    public int PrefixLength => Mask.GetAddressBytes().Sum(b => BitOperations.PopCount(b));
    public override string ToString() => $"{Name} – {Address}/{PrefixLength} (broadcast {Broadcast})";
}

public static class TCNetNetwork
{
    /// <summary>Up, non-loopback IPv4 interfaces.</summary>
    public static IReadOnlyList<NetworkInterfaceInfo> Interfaces()
    {
        var list = new List<NetworkInterfaceInfo>();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException) { return list; }

        foreach (var ni in all)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = ua.IPv4Mask is { } m && !m.Equals(IPAddress.Any) ? m : IPAddress.Parse("255.255.255.0");
                    list.Add(new NetworkInterfaceInfo(ni.Name, ua.Address, mask, Broadcast(ua.Address, mask)));
                }
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or NotImplementedException) { }
        }
        return list;
    }

    /// <summary>address | ~mask.</summary>
    public static IPAddress Broadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(a);
    }

    public static HashSet<IPAddress> LocalAddresses()
    {
        var set = new HashSet<IPAddress> { IPAddress.Loopback };
        foreach (var i in Interfaces()) set.Add(i.Address);
        return set;
    }
}

/// <summary>
/// Opt-OUT tip: when the master disconnects, the Auto node with the highest uptime (including timestamp) becomes master.
/// Uptimes within <see cref="TieWindowSeconds"/> tie and go to the lower Node ID.
/// </summary>
public static class MasterElection
{
    public const int TieWindowSeconds = 2;

    public readonly record struct Candidate(ushort NodeId, ushort Uptime, uint Timestamp);

    /// <summary>True when <paramref name="a"/> beats <paramref name="b"/>.</summary>
    public static bool Beats(Candidate a, Candidate b)
    {
        int diff = a.Uptime - b.Uptime;
        if (Math.Abs(diff) > TieWindowSeconds) return diff > 0;
        return a.NodeId < b.NodeId;
    }

    public static Candidate? Winner(IEnumerable<Candidate> candidates)
    {
        Candidate? best = null;
        foreach (var c in candidates)
            if (best is null || Beats(c, best.Value)) best = c;
        return best;
    }

    public static Candidate Of(RemoteNode n) => new(n.NodeId, n.Uptime, n.LastTimestamp);
}
