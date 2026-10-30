using System.Runtime.InteropServices;
namespace Bomber.DesktopLauncher;

internal static class Native
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr owner,string text,string title,uint type);
}
