using Bomber.Game;
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
    ConfigureBuilder = builder => builder.Services.AddSingleton<IRealtimeGame, BomberRealtimeGame>(),
});
app.MapGet("/game", () => Results.Ok(new { game = "blast", client = "Godot C#", protocol = "/realtime/v1", tickRate = LocalMatch.TickRate })).AllowAnonymous();
await app.RunAsync();
