using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Bomber.DesktopLauncher;

/// <summary>Owns only the shortcut flags for accessibility features that are off.</summary>
internal sealed class WindowsAccessibilityHotkeys(WindowsAccessibilityHotkeys.ISettings settings) : IDisposable
{
    internal enum Feature { Sticky, Filter, Toggle }
    internal readonly record struct Values(uint Flags,uint Wait=0,uint Delay=0,uint Repeat=0,uint Bounce=0);
    internal interface ISettings
    {
        Values Read(Feature feature);
        void Write(Feature feature,Values value);
    }
    internal const uint Enabled=1,ShortcutFlags=4|8;
    private readonly Dictionary<Feature,Values> _saved=[];
    private bool _focused;

    internal void SetFocused(bool focused)
    {
        if(_focused==focused)return;
        if(!focused){Restore();_focused=false;return;}
        // Read every original value before changing any of them.
        var originals=Enum.GetValues<Feature>().ToDictionary(feature=>feature,settings.Read);
        try
        {
            foreach(var (feature,value)in originals)
                if((value.Flags&Enabled)==0&&(value.Flags&ShortcutFlags)!=0)
                {
                    _saved.Add(feature,value);
                    settings.Write(feature,value with{Flags=value.Flags&~ShortcutFlags});
                }
            _focused=true;
        }
        catch{Restore();throw;}
    }

    private void Restore()
    {
        Exception? failure=null;
        foreach(var (feature,saved)in _saved.ToArray())
        {
            try
            {
                // Do not overwrite unrelated flags/timings changed by the user.
                var current=settings.Read(feature);
                settings.Write(feature,current with{Flags=(current.Flags&~ShortcutFlags)|(saved.Flags&ShortcutFlags)});
                _saved.Remove(feature);
            }
            catch(Exception error){failure??=error;}
        }
        if(failure is not null)throw failure;
    }

    public void Dispose()=>Restore();

    /// <summary>The launcher survives an engine crash and restores on the same owner thread.</summary>
    internal static IDisposable Track(int enginePid)=>new FocusLifetime(enginePid);

    private sealed class FocusLifetime : IDisposable
    {
        private readonly ManualResetEventSlim _stop=new();
        private readonly Thread _thread;
        internal FocusLifetime(int pid)
        {
            _thread=new Thread(()=>
            {
                try{Run(pid);}
                catch(Exception error){Console.Error.WriteLine("ERROR: BLAST_ACCESSIBILITY_HOTKEY_GUARD_FAILED "+error.Message);}
            }){IsBackground=true,Name="Blast accessibility focus"};
            _thread.Start();
        }
        private void Run(int pid)
        {
            using var mutex=new Mutex(false,@"Local\BlastCircuitAccessibilityHotkeys");
            var shortcuts=new WindowsAccessibilityHotkeys(new NativeSettings());bool owned=false;
            try
            {
                while(!_stop.Wait(25))
                {
                    NativeSettings.GetWindowThreadProcessId(NativeSettings.GetForegroundWindow(),out uint foreground);
                    if(foreground==(uint)pid)
                    {
                        if(!owned)
                        {
                            try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
                        }
                        if(owned)shortcuts.SetFocused(true);
                    }
                    else if(owned)
                    {
                        shortcuts.SetFocused(false);mutex.ReleaseMutex();owned=false;
                    }
                }
            }
            catch(Exception error){Console.Error.WriteLine("ERROR: BLAST_ACCESSIBILITY_HOTKEY_GUARD_FAILED "+error.Message);}
            finally
            {
                try{shortcuts.Dispose();}
                catch(Exception error){Console.Error.WriteLine("ERROR: BLAST_ACCESSIBILITY_HOTKEY_RESTORE_FAILED "+error.Message);}
                finally{if(owned)mutex.ReleaseMutex();}
            }
        }
        public void Dispose()
        {
            _stop.Set();
            if(!_thread.Join(2000))Console.Error.WriteLine("ERROR: BLAST_ACCESSIBILITY_HOTKEY_RESTORE_TIMEOUT");
            else _stop.Dispose();
        }
    }

    internal sealed class NativeSettings : ISettings
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Keys { public uint Size,Flags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Filter { public uint Size,Flags,Wait,Delay,Repeat,Bounce; }
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool Info(uint action,uint size,ref Keys value,uint updateFlags);
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool Info(uint action,uint size,ref Filter value,uint updateFlags);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        private static uint GetAction(Feature feature)=>feature switch{Feature.Sticky=>0x003A,Feature.Filter=>0x0032,Feature.Toggle=>0x0034,_=>throw new ArgumentOutOfRangeException(nameof(feature))};
        public Values Read(Feature feature)
        {
            if(feature==Feature.Filter)
            {
                var value=new Filter{Size=(uint)Marshal.SizeOf<Filter>()};
                if(!Info(GetAction(feature),value.Size,ref value,0))throw new Win32Exception(Marshal.GetLastWin32Error());
                return new(value.Flags,value.Wait,value.Delay,value.Repeat,value.Bounce);
            }
            var keys=new Keys{Size=(uint)Marshal.SizeOf<Keys>()};
            if(!Info(GetAction(feature),keys.Size,ref keys,0))throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(keys.Flags);
        }
        public void Write(Feature feature,Values input)
        {
            // Zero is deliberate: never SPIF_UPDATEINIFILE or SPIF_SENDCHANGE.
            if(feature==Feature.Filter)
            {
                var value=new Filter{Size=(uint)Marshal.SizeOf<Filter>(),Flags=input.Flags,Wait=input.Wait,Delay=input.Delay,Repeat=input.Repeat,Bounce=input.Bounce};
                if(!Info(GetAction(feature)+1,value.Size,ref value,0))throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            var keys=new Keys{Size=(uint)Marshal.SizeOf<Keys>(),Flags=input.Flags};
            if(!Info(GetAction(feature)+1,keys.Size,ref keys,0))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
