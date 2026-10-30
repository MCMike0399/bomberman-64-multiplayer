using System.Text.Json;
using Bomber.Client.Startup;
using Bomber.DesktopLauncher;
using Hotkeys=Bomber.DesktopLauncher.WindowsAccessibilityHotkeys;

internal static class WindowsHotkeyChecks
{
    private sealed class Settings : Hotkeys.ISettings
    {
        internal readonly Dictionary<Hotkeys.Feature,Hotkeys.Values> Values=new()
        {
            [Hotkeys.Feature.Sticky]=new(0x2c),
            [Hotkeys.Feature.Filter]=new(0x4d,7,11,13,17), // Feature enabled: must never be changed.
            [Hotkeys.Feature.Toggle]=new(0x10c)
        };
        internal readonly List<Hotkeys.Feature> Writes=[];
        internal bool FailToggle;
        public Hotkeys.Values Read(Hotkeys.Feature feature)=>Values[feature];
        public void Write(Hotkeys.Feature feature,Hotkeys.Values value)
        {
            if(feature==Hotkeys.Feature.Toggle&&FailToggle){FailToggle=false;throw new IOException("Injected SPI failure");}
            Writes.Add(feature);Values[feature]=value;
        }
    }

    internal static void Run(Action<bool,string> check,IEnumerable<string> args)
    {
        var settings=new Settings();var original=settings.Values.ToDictionary();
        using(var guard=new Hotkeys(settings))
        {
            guard.SetFocused(true);
            foreach(var (feature,value)in original)
                check(settings.Read(feature)==((value.Flags&Hotkeys.Enabled)!=0?value:value with{Flags=value.Flags&~Hotkeys.ShortcutFlags}),"Only shortcut flags of disabled accessibility features change");
            int writes=settings.Writes.Count;guard.SetFocused(true);
            check(settings.Writes.Count==writes,"Repeated focus does not overwrite the saved original values");
            check(!settings.Writes.Contains(Hotkeys.Feature.Filter),"Enabled Filter Keys remains untouched");
            guard.SetFocused(false);
            check(original.All(pair=>settings.Read(pair.Key)==pair.Value),"Focus loss restores all saved shortcut values and timings");
            guard.SetFocused(true);
        }
        check(original.All(pair=>settings.Read(pair.Key)==pair.Value),"Exit while focused restores the saved values");
        settings.FailToggle=true;
        using(var guard=new Hotkeys(settings))
        {
            try{guard.SetFocused(true);check(false,"SPI failure must be reported");}
            catch(IOException){check(original.All(pair=>settings.Read(pair.Key)==pair.Value),"Partial suppression failure restores earlier successful changes");}
        }
        using(var guard=new Hotkeys(settings))
        {
            guard.SetFocused(true);
            settings.Values[Hotkeys.Feature.Sticky]=settings.Read(Hotkeys.Feature.Sticky)with{Flags=settings.Read(Hotkeys.Feature.Sticky).Flags|0x80};
            guard.SetFocused(false);
            check(settings.Read(Hotkeys.Feature.Sticky).Flags==(original[Hotkeys.Feature.Sticky].Flags|0x80),"Restoration preserves unrelated concurrent flag changes");
        }
        if(!OperatingSystem.IsWindows())return;
        var native=new Hotkeys.NativeSettings();var before=Enum.GetValues<Hotkeys.Feature>().ToDictionary(feature=>feature,native.Read);
        using(var guard=new Hotkeys(native))
        {
            guard.SetFocused(true);
            foreach(var (feature,value)in before)
                check(native.Read(feature)==((value.Flags&Hotkeys.Enabled)!=0?value:value with{Flags=value.Flags&~Hotkeys.ShortcutFlags}),"Actual Windows SPI suppresses only disabled-feature shortcut flags");
            guard.SetFocused(false);
            check(before.All(pair=>native.Read(pair.Key)==pair.Value),"Actual Windows SPI restores values on focus loss");
            guard.SetFocused(true);
        }
        var after=before.Keys.ToDictionary(feature=>feature,native.Read);
        check(before.All(pair=>after[pair.Key]==pair.Value),"Actual Windows SPI restores saved values on exit");
        if(WindowsLaunchGuard.Argument(args,"--hotkey-receipt")is {} receipt)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(receipt))!);
            File.WriteAllText(receipt,JsonSerializer.Serialize(new{passed=true,scope="Actual Windows SPI suppression and restoration control",sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA"),systemParametersInfoUpdateFlags=0,before=before.ToDictionary(pair=>pair.Key.ToString(),pair=>pair.Value),after=after.ToDictionary(pair=>pair.Key.ToString(),pair=>pair.Value)}));
        }
    }
}
