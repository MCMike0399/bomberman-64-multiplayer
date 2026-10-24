using Burbuja.Core.Abstractions.Plugin;
using Burbuja.Realtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bomber.Cluster;

/// <summary>Optional deployment policy; the kernel retains capacity claims and fleet failure handling.</summary>
public sealed class ClusterPlugin : IBurbujaPlugin
{
    public PluginManifest Manifest { get; } = new(
        "Blast.Cluster", "Blast Cluster Placement", "1.0.0", PluginContracts.Version,
        "Bomber.Cluster.dll", typeof(ClusterPlugin).FullName!, PluginLoadKind.Dynamic,
        ["blast.cluster.placement"], [], [], ["all", "worker"], "BlastCluster", false, [], 210);

    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<ILobbyPlacementStrategy>(sp => new PreferredServerPlacement(
            sp.GetRequiredService<IConfiguration>().GetSection("BlastCluster:ServerOrder")
                .GetChildren().Select(c => c.Value ?? "").ToArray()));
}

/// <summary>Always use the first eligible configured server, including after a primary recovers.</summary>
public sealed class PreferredServerPlacement : ILobbyPlacementStrategy
{
    private readonly Dictionary<string, int> _rank;
    public string Name => "blast-primary-fallback";

    public PreferredServerPlacement(IReadOnlyList<string> serverOrder)
    {
        if (serverOrder.Count == 0 || serverOrder.Any(string.IsNullOrWhiteSpace) ||
            serverOrder.Distinct(StringComparer.Ordinal).Count() != serverOrder.Count)
            throw new ArgumentException("BlastCluster:ServerOrder must contain unique, nonempty server IDs.", nameof(serverOrder));
        _rank = serverOrder.Select((id, rank) => (id, rank)).ToDictionary(v => v.id, v => v.rank, StringComparer.Ordinal);
    }

    public string? Choose(IReadOnlyList<LobbyServerStatus> candidates, PlacementRequest request) =>
        candidates.Where(c => c.HasCapacity && _rank.ContainsKey(c.ServerId))
            .OrderBy(c => _rank[c.ServerId]).Select(c => c.ServerId).FirstOrDefault();
}
