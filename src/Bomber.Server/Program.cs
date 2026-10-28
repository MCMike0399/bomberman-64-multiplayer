using Bomber.Game.Matches;
using Bomber.Game.Online;
using Burbuja.Hosting;
using Burbuja.Realtime.Abstractions;

await using var app = BurbujaApplication.CreateApp(new WebApplicationOptions
{
    Args = args,
    ApplicationName = typeof(Program).Assembly.GetName().Name,
    ContentRootPath = AppContext.BaseDirectory,
}, new BurbujaHostOptions
{
    DefaultLandingRoute = "/game",
    ConfigureBuilder = builder => {
        // Owned QA servers may lengthen the peer host lease; production keeps six seconds.
        builder.Services.AddSingleton(Bomber.Game.Online.Rooms.PeerHostLease.From(builder.Configuration.GetValue<double?>("Blast:PeerHostLeaseSeconds")));
        builder.Services.AddSingleton(services => new Bomber.Game.Online.Rooms.PeerRoomTrace(message =>
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Bomber.PeerRoom").LogInformation("{PeerRoomEvent}", message)));
        builder.Services.AddSingleton<IRealtimeGame, BomberRealtimeGame>();
        builder.Services.AddSingleton<IRealtimeGame, PublicLobbyGame>();
        builder.Services.AddSingleton<IRealtimeGame, PeerRoomGame>();
        builder.Services.AddSingleton<IRealtimeGame, AdaptiveRoomGame>();
        builder.Services.AddSingleton<IRealtimeGame, PublicAdaptiveRoomGame>();
        builder.Services.AddHostedService<Bomber.Server.PerformanceReporter>();
    },
});
app.UseMiddleware<Bomber.Server.ClientProtocolMiddleware>();
app.MapGet("/game", () => Results.Ok(new { game = "blast", client = "Godot C#", protocol = "/realtime/v1", tickRate = LocalMatch.TickRate,
    release = new { version = OnlineProtocol.ReleaseVersion, build = OnlineProtocol.ReleaseBuild,
        protocolRevision = OnlineProtocol.Revision, admissionToken = OnlineProtocol.WireRevision, revision = OnlineProtocol.SourceRevision } })).AllowAnonymous();
await app.RunAsync();
