using Godot;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
namespace BlastCircuit.App;

/// <summary>Device-local choices, including the name used when joining a server.</summary>
public sealed class PlayerPreferences
{
    public string PlayerName { get; set; } = "";
    public string ServerUrl { get; set; } = Bomber.Client.NetworkSession.DefaultServerUrl;
    public bool Vibration { get; set; }
    public bool AutomaticUpdates { get; set; } = true;
    public float Volume { get; set; } = .8f;
    public StageTime StageLighting {get;set;}=StageTime.Midday;
    public GraphicsPreset Graphics {get;set;}=GraphicsPreset.Medium;
    public static PlayerPreferences Load(string path)
    {
        var value = new PlayerPreferences();
        var file = new ConfigFile();
        if (file.Load(path) != Error.Ok) return value;
        var name=file.GetValue("online","name","");
        var server=file.GetValue("online","server",value.ServerUrl);
        if(name.VariantType==Variant.Type.String)value.PlayerName=name.AsString();
        if(server.VariantType==Variant.Type.String)value.ServerUrl=server.AsString();
        var lighting=file.GetValue("video","stageLighting","midday");
        var graphics=file.GetValue("video","graphicsPreset","medium");
        if(graphics.VariantType==Variant.Type.String)value.Graphics=GraphicsQuality.FromWire(graphics.AsString());
        if(lighting.VariantType==Variant.Type.String)value.StageLighting=BlastCircuit.Arena.StageLighting.FromWire(lighting.AsString());
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
        file.SetValue("online","name",PlayerName);
        file.SetValue("online","server",ServerUrl);
        file.SetValue("player", "vibration", Vibration);
        file.SetValue("player", "automaticUpdates", AutomaticUpdates);
        file.SetValue("player", "volume", Volume);
        file.SetValue("video","stageLighting",BlastCircuit.Arena.StageLighting.ToWire(StageLighting));
        file.SetValue("video","graphicsPreset",GraphicsQuality.ToWire(Graphics));
        return file.Save(path);
    }
}
