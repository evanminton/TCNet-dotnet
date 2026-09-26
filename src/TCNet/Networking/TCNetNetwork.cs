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
/// with the highest uptime becomes the new master. Uptimes within <see cref="UptimeTolerance"/> of each
/// other count as equal (remote uptimes are up to one Opt-IN interval old); ties go to the lower Node ID,
/// then the lower node name, so every node reaches the same verdict.
/// </summary>
public static class TCNetMasterElection
{
    /// <summary>Uptime difference (seconds) treated as a tie.</summary>
    public const int UptimeTolerance = 2;

    /// <summary>The Auto node that should become master, or null.</summary>
    public static TCNetRemoteNode? ChooseMaster(IEnumerable<TCNetRemoteNode> nodes, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        TCNetRemoteNode? best = null;
        foreach (var n in nodes.Where(n => n.NodeType == NodeType.Auto))
            if (best is null || Outranks(EstimatedUptime(n, t), n.NodeId, n.NodeName, EstimatedUptime(best, t), best.NodeId, best.NodeName))
                best = n;
        return best;
    }

    /// <summary>True if no master is present and the local Auto node outranks every Auto candidate.</summary>
    public static bool ShouldPromote(ushort selfUptime, ushort selfNodeId, string selfName, IEnumerable<TCNetRemoteNode> nodes, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        var all = nodes.ToList();
        if (all.Any(n => n.NodeType == NodeType.Master)) return false;
        return all.Where(n => n.NodeType == NodeType.Auto)
                  .All(n => Outranks(selfUptime, selfNodeId, selfName, EstimatedUptime(n, t), n.NodeId, n.NodeName));
    }

    /// <summary>True if a node that became master by election should step back because <paramref name="other"/> outranks it.</summary>
    public static bool ShouldDemote(ushort selfUptime, ushort selfNodeId, string selfName, TCNetRemoteNode other, DateTime? now = null) =>
        other.NodeType == NodeType.Master
        && !Outranks(selfUptime, selfNodeId, selfName, EstimatedUptime(other, now ?? DateTime.UtcNow), other.NodeId, other.NodeName);

    /// <summary>Remote uptime advanced by the time since its last Opt-IN.</summary>
    private static long EstimatedUptime(TCNetRemoteNode n, DateTime now) =>
        n.Uptime + (n.LastOptIn is { } seen && now > seen ? (long)(now - seen).TotalSeconds : 0);

    private static bool Outranks(long uptimeA, ushort idA, string nameA, long uptimeB, ushort idB, string nameB)
    {
        if (Math.Abs(uptimeA - uptimeB) > UptimeTolerance) return uptimeA > uptimeB;
        if (idA != idB) return idA < idB;
        return string.CompareOrdinal(nameA, nameB) < 0;
    }
}
