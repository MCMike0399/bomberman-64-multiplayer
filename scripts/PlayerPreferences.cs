using Godot;
namespace BlastCircuit;

/// <summary>Device-local player choices; never sent to the game authority.</summary>
public sealed class PlayerPreferences
{
    public bool Vibration { get; set; }
    public bool AutomaticUpdates { get; set; } = true;
    public float Volume { get; set; } = .8f;
    public static PlayerPreferences Load(string path)
    {
        var value = new PlayerPreferences();
        var file = new ConfigFile();
        if (file.Load(path) != Error.Ok) return value;
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
        file.SetValue("player", "vibration", Vibration);
        file.SetValue("player", "automaticUpdates", AutomaticUpdates);
        file.SetValue("player", "volume", Volume);
        return file.Save(path);
    }
}
