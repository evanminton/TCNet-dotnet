using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TCNet.Networking;

/// <summary>An IPv4 interface usable for TCNet.</summary>
public sealed record TCNetInterface(string Name, string Description, IPAddress Address, IPAddress Mask, IPAddress Broadcast)
{
    public override string ToString() => $"{Name} – {Address}/{MaskBits(Mask)} (bcast {Broadcast})";

    private static int MaskBits(IPAddress mask) => mask.GetAddressBytes().Sum(b => System.Numerics.BitOperations.PopCount(b));
}

/// <summary>Network interface helpers.</summary>
public static class TCNetNetwork
{
    /// <summary>Up, non-loopback IPv4 interfaces with their directed broadcast addresses.</summary>
    public static IReadOnlyList<TCNetInterface> GetInterfaces()
    {
        var list = new List<TCNetInterface>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = ua.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                    if (mask.Equals(IPAddress.Any)) mask = IPAddress.Parse("255.255.255.0");
                    list.Add(new TCNetInterface(ni.Name, ni.Description, ua.Address, mask, DirectedBroadcast(ua.Address, mask)));
                }
            }
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return list;
    }

    /// <summary>address | ~mask.</summary>
    public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = new byte[4];
        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
    }

    /// <summary>All local IPv4 addresses plus loopback.</summary>
    public static HashSet<IPAddress> LocalAddresses()
    {
        var set = new HashSet<IPAddress> { IPAddress.Loopback };
        foreach (var i in GetInterfaces()) set.Add(i.Address);
        return set;
    }
}

/// <summary>
/// Master election from the Opt-OUT tip: when a master disconnects, the node running as Auto (type 1)
/// with the highest uptime (then timestamp) becomes the new master.
/// </summary>
public static class TCNetMasterElection
{
    /// <summary>The Auto node that should become master, or null.</summary>
    public static TCNetRemoteNode? ChooseMaster(IEnumerable<TCNetRemoteNode> nodes) =>
        nodes.Where(n => n.NodeType == NodeType.Auto)
             .OrderByDescending(n => Score(n.Uptime, n.LastTimestamp))
             .FirstOrDefault();

    /// <summary>True if no master is present and the local Auto node outranks every Auto candidate.</summary>
    public static bool ShouldPromote(ushort selfUptime, uint selfTimestamp, IEnumerable<TCNetRemoteNode> nodes)
    {
        var all = nodes.ToList();
        if (all.Any(n => n.NodeType == NodeType.Master)) return false;
        long self = Score(selfUptime, selfTimestamp);
        return all.Where(n => n.NodeType == NodeType.Auto).All(n => Score(n.Uptime, n.LastTimestamp) < self);
    }

    private static long Score(ushort uptime, uint timestamp) => uptime * 1_000_000L + timestamp;
}
