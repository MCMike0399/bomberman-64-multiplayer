using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace BlastCircuit.App;

public sealed partial class GodotPeerLink
{
    internal static JsonElement SafeDiagnosticSnapshot(object diagnostics)
    {
        var safe=JsonSerializer.SerializeToNode(diagnostics)!.AsObject();
        foreach(var candidate in safe["candidates"]!.AsArray())
        {candidate!.AsObject().Remove("address");candidate.AsObject().Remove("port");}
        safe["endpointScope"]="Candidate/SDP hashes, order and address family/scope only; addresses and ports omitted from QA receipts.";
        return JsonSerializer.SerializeToElement(safe);
    }

    /// <summary>Non-identifying endpoint categories only; never resolves a hostname.</summary>
    internal static (string Family,string Scope) CandidateAddressSummary(string? address)
    {
        if(string.IsNullOrWhiteSpace(address))return ("unknown","unknown");
        if(!IPAddress.TryParse(address,out var ip))return ("hostname","unresolved");
        string family=ip.AddressFamily==AddressFamily.InterNetwork?"IPv4":"IPv6";
        if(ip.IsIPv4MappedToIPv6){family="IPv6-mapped-IPv4";ip=ip.MapToIPv4();}
        if(IPAddress.IsLoopback(ip))return (family,"loopback");
        if(ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any))return (family,"unspecified");
        var bytes=ip.GetAddressBytes();
        if(ip.AddressFamily==AddressFamily.InterNetwork)
        {
            if(bytes[0]==10||bytes[0]==172&&bytes[1] is >=16 and <=31||bytes[0]==192&&bytes[1]==168)
                return (family,"private");
            if(bytes[0]==169&&bytes[1]==254)return (family,"link-local");
            if(bytes[0]==100&&bytes[1] is >=64 and <=127)return (family,"shared");
            if(bytes[0] is >=224 and <=239)return (family,"multicast");
            if(ip.Equals(IPAddress.Broadcast))return (family,"broadcast");
        }
        else
        {
            if(ip.IsIPv6LinkLocal)return (family,"link-local");
            if((bytes[0]&0xfe)==0xfc)return (family,"unique-local");
            if(ip.IsIPv6Multicast)return (family,"multicast");
        }
        return (family,"global-or-other"); // Category, never a reachability claim.
    }
}
