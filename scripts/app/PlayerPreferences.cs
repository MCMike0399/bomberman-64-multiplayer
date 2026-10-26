using Godot;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
namespace BlastCircuit.App;

/// <summary>Device-local choices, including the name used when joining a server.</summary>
public sealed class PlayerPreferences
{
    public HashSet<string> CompletedTutorials {get;}=[];
    public string PlayerName { get; set; } = "";
    public string ServerUrl { get; set; } = Bomber.Client.NetworkSession.DefaultServerUrl;
    public bool Vibration { get; set; }
    public bool AutomaticUpdates { get; set; } = true;
    public float Volume { get; set; } = .8f;
    public StageTime StageLighting {get;set;}=StageTime.Midday;
    public bool Fullscreen {get;set;}=true;
    public Vector2I Resolution {get;set;}
    public GraphicsPreset Graphics {get;set;}=GraphicsPreset.Medium;
    public static PlayerPreferences Load(string path)
    {
        var value = new PlayerPreferences();
        var file = new ConfigFile();
        if (file.Load(path) != Error.Ok) return value;
        var completed=file.GetValue("tutorial","completed","");
        if(completed.VariantType==Variant.Type.String)value.CompletedTutorials.UnionWith(completed.AsString().Split(',').Where(Bomber.Game.TutorialCourse.Contains));
        var name=file.GetValue("online","name","");
        var server=file.GetValue("online","server",value.ServerUrl);
        if(name.VariantType==Variant.Type.String)value.PlayerName=name.AsString();
        if(server.VariantType==Variant.Type.String)value.ServerUrl=server.AsString();
        var lighting=file.GetValue("video","stageLighting","midday");
        var graphics=file.GetValue("video","graphicsPreset","medium");
        if(graphics.VariantType==Variant.Type.String)value.Graphics=GraphicsQuality.FromWire(graphics.AsString());
        if(lighting.VariantType==Variant.Type.String)value.StageLighting=BlastCircuit.Arena.StageLighting.FromWire(lighting.AsString());
        var fullscreen=file.GetValue("video","fullscreen",true);
        if(fullscreen.VariantType==Variant.Type.Bool)value.Fullscreen=fullscreen.AsBool();
        var resolution=file.GetValue("video","resolution",Vector2I.Zero);
        if(resolution.VariantType==Variant.Type.Vector2I)value.Resolution=DisplayModeController.ValidResolution(resolution.AsVector2I());
        var automatic = file.GetValue("player", "automaticUpdates", true);
        if (automatic.VariantType == Variant.Type.Bool) value.AutomaticUpdates = automatic.AsBool();
        var vibration = file.GetValue("player", "vibration", false);
        var volume = file.GetValue("player", "volume", .8f);
        if (vibration.VariantType == Variant.Type.Bool) value.Vibration = vibration.AsBool();
        if (volume.VariantType is Variant.Type.Float or Variant.Type.Int)
        { var number = volume.AsSingle(); if (float.IsFinite(number)) value.Volume = Math.Clamp(number, 0, 1); }
        return value;
    }
    public Error Save(string path)
    {
        var file = new ConfigFile();
        file.SetValue("tutorial","completed",string.Join(',',CompletedTutorials.Order()));
        file.SetValue("online","name",PlayerName);
        file.SetValue("online","server",ServerUrl);
        file.SetValue("player", "vibration", Vibration);
        file.SetValue("player", "automaticUpdates", AutomaticUpdates);
        file.SetValue("player", "volume", Volume);
        file.SetValue("video","stageLighting",BlastCircuit.Arena.StageLighting.ToWire(StageLighting));
        file.SetValue("video","graphicsPreset",GraphicsQuality.ToWire(Graphics));
        file.SetValue("video","fullscreen",Fullscreen);
        file.SetValue("video","resolution",DisplayModeController.ValidResolution(Resolution));
        return file.Save(path);
    }
}
