using System.Runtime.InteropServices;
using Godot;
namespace BlastCircuit.Testing;

/// <summary>Exercise this application's real AppKit controls without posting global input.</summary>
internal static class MacQuitControl
{
    private const string ObjC="/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern nint Send(nint target,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern nint SendIndex(nint target,nint selector,nint argument);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern void Perform(nint target,nint selector,nint argument);
    private static nint Get(nint target,string selector)=>Send(target,sel_registerName(selector));

    internal static void Invoke(string action)
    {
        if(OS.GetName()!="macOS"||OS.GetThreadCallerId()!=OS.GetMainThreadId())
            throw new InvalidOperationException("Native Mac quit controls require the Mac main thread");
        nint app=Get(objc_getClass("NSApplication"),"sharedApplication");
        if(action=="close-button")
        {
            nint window=Get(app,"keyWindow");if(window==0)window=Get(app,"mainWindow");
            nint button=window==0?0:SendIndex(window,sel_registerName("standardWindowButton:"),0);
            if(button==0)throw new InvalidOperationException("No native red close button");
            GD.Print("BLAST_MAC_NATIVE_QUIT action=close-button route=NSWindowCloseButton.performClick");
            Perform(button,sel_registerName("performClick:"),0);
            return;
        }
        if(action!="cmd-q-menu")throw new ArgumentException("Unknown native Mac quit action");
        nint menu=Get(app,"mainMenu"),items=Get(menu,"numberOfItems");
        for(nint top=0;top<items;top++)
        {
            nint submenu=Get(SendIndex(menu,sel_registerName("itemAtIndex:"),top),"submenu");
            if(submenu==0)continue;
            nint count=Get(submenu,"numberOfItems");
            for(nint index=0;index<count;index++)
            {
                nint item=SendIndex(submenu,sel_registerName("itemAtIndex:"),index);
                string? key=Marshal.PtrToStringUTF8(Get(Get(item,"keyEquivalent"),"UTF8String"));
                if(key!="q"||(Get(item,"keyEquivalentModifierMask")&(1<<20))==0)continue;
                GD.Print("BLAST_MAC_NATIVE_QUIT action=cmd-q-menu route=NSMenu.CommandQ.performActionForItemAtIndex");
                Perform(submenu,sel_registerName("performActionForItemAtIndex:"),index);
                return;
            }
        }
        throw new InvalidOperationException("No native Command-Q menu item");
    }
}
