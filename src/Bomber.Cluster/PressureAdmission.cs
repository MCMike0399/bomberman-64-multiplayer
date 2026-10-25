using Burbuja.Realtime.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bomber.Cluster;

/// <summary>Refresh advice outside game loops. Existing claims survive pressure; only new work is deferred.</summary>
public sealed class PressureAdmission : BackgroundService, ISessionResourceAdmission
{
    private readonly PressureSnapshot _snapshot;
    private readonly string[] _servers;
    private volatile bool _allowed = true;

    public PressureAdmission(IConfiguration config)
    {
        _snapshot = new PressureSnapshot(config["BlastCluster:AdmissionFile"]);
        _servers = config.GetSection("Realtime:Servers").GetChildren().Select(c => c["Id"] ?? "").ToArray();
        if (_servers.Length == 0) _servers = [config["Kernel:NodeId"] ?? "local"];
    }
    public bool CanIncrease(string resource) => _allowed;
    public void Refresh()
    {
        var excluded = _snapshot.ExcludedServers();
        _allowed = !_servers.Any(excluded.Contains);
    }
    public override Task StartAsync(CancellationToken ct) { Refresh(); return base.StartAsync(ct); }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) Refresh(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
