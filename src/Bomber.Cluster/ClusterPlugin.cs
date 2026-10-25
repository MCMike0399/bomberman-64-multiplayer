using Burbuja.Core.Abstractions.Plugin;
using Burbuja.Realtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bomber.Cluster;

/// <summary>Optional deployment policy; the kernel retains capacity claims and fleet failure handling.</summary>
public sealed class ClusterPlugin : IBurbujaPlugin
{
    public PluginManifest Manifest { get; } = new(
        "Blast.Cluster", "Blast Cluster Placement", "1.2.0", PluginContracts.Version,
        "Bomber.Cluster.dll", typeof(ClusterPlugin).FullName!, PluginLoadKind.Dynamic,
        ["blast.cluster.placement"], [], [], ["all", "worker"], "BlastCluster", false, [], 210);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILobbyPlacementStrategy>(sp => new PreferredServerPlacement(
            sp.GetRequiredService<IConfiguration>().GetSection("BlastCluster:ServerOrder")
                .GetChildren().Select(c => c.Value ?? "").ToArray(),
            new PressureSnapshot(sp.GetRequiredService<IConfiguration>()["BlastCluster:AdmissionFile"])));
        services.AddSingleton<PressureAdmission>();
        services.AddSingleton<ISessionResourceAdmission>(sp => sp.GetRequiredService<PressureAdmission>());
        services.AddHostedService(sp => sp.GetRequiredService<PressureAdmission>());
    }
}

/// <summary>Always use the first eligible configured server, including after a primary recovers.</summary>
public sealed class PreferredServerPlacement : ILobbyPlacementStrategy
{
    private readonly Dictionary<string, int> _rank;
    private readonly PressureSnapshot _pressure;
    public string Name => "blast-primary-fallback";

    public PreferredServerPlacement(IReadOnlyList<string> serverOrder, PressureSnapshot? pressure = null)
    {
        if (serverOrder.Count == 0 || serverOrder.Any(string.IsNullOrWhiteSpace) ||
            serverOrder.Distinct(StringComparer.Ordinal).Count() != serverOrder.Count)
            throw new ArgumentException("BlastCluster:ServerOrder must contain unique, nonempty server IDs.", nameof(serverOrder));
        _rank = serverOrder.Select((id, rank) => (id, rank)).ToDictionary(v => v.id, v => v.rank, StringComparer.Ordinal);
        _pressure = pressure ?? new PressureSnapshot(null);
    }

    public string? Choose(IReadOnlyList<LobbyServerStatus> candidates, PlacementRequest request)
    {
        var excluded = _pressure.ExcludedServers();
        string preference = request.GameId == "blast-room" ? request.Settings.GetValueOrDefault("hosting", "auto") : "auto";
        bool adaptive = request.GameId is "blast-room" or "blast-online";
        return candidates.Where(c => c.HasCapacity && _rank.ContainsKey(c.ServerId) && !excluded.Contains(c.ServerId) &&
                (!adaptive || SupportsPreference(c, preference)))
            .OrderBy(c => adaptive ? ResourceRank(c, preference) : 0)
            .ThenBy(c => _rank[c.ServerId]).Select(c => c.ServerId).FirstOrDefault();
    }

    private static bool SupportsPreference(LobbyServerStatus server, string preference) => server.ResourceLimits is not { } limits ||
        (preference switch { "peer" => limits.GetValueOrDefault("peer-relay") > 0, "server" => limits.GetValueOrDefault("simulation") > 0,
            _ => limits.GetValueOrDefault("simulation") > 0 || limits.GetValueOrDefault("peer-relay") > 0 });
    private static int ResourceRank(LobbyServerStatus server, string preference)
    {
        if (server.AvailableResources is not { } available) return 0;
        if (preference == "peer") return available.GetValueOrDefault("peer-relay") > 0 ? 0 : 2;
        if (available.GetValueOrDefault("simulation") > 0) return 0;
        return preference != "server" && available.GetValueOrDefault("peer-relay") > 0 ? 1 : 2;
    }
}
