using System.Diagnostics;
using System.Globalization;

namespace Bomber.Client.Updates;

/// <summary>Exact process birth identity shared across embedded and standalone runtimes.</summary>
public static class DesktopProcessIdentity
{
    public static string StartToken(Process process)
    {
        if (!OperatingSystem.IsLinux())
            return "start:" + process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        // Process.StartTime converts boot-relative time using a process-local
        // wall-clock estimate. Compare the kernel counter itself across runtimes.
        string stat = File.ReadAllText($"/proc/{process.Id}/stat");
        int end = stat.LastIndexOf(')');
        string[] fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (end < 0 || fields.Length <= 19 || !ulong.TryParse(fields[19], out _))
            throw new IOException("Missing Linux process birth counter.");
        string boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        if (!Guid.TryParse(boot, out _)) throw new IOException("Missing Linux boot identity.");
        return "linux:" + boot + ":" + fields[19];
    }

    public static bool Matches(Process process, string token)
    {
        if (process.HasExited) return false;
        try { return StartToken(process) == token && !process.HasExited; }
        catch (IOException) when (process.HasExited) { return false; }
    }

    public static string Marker(Process process) => process.Id + " " + StartToken(process);

    public static void WriteMarker(string path, Process process)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Path.GetFileName(path) + ".tmp");
        File.WriteAllText(temporary, Marker(process));
        File.Move(temporary, path, overwrite: true);
    }

    public static Process OpenMarker(string path)
    {
        string[] parts = File.ReadAllText(path).Trim().Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int pid) || pid <= 0)
            throw new InvalidDataException("Missing PID/birth process identity.");
        var process = Process.GetProcessById(pid);
        try
        {
            if (Matches(process, parts[1])) return process;
            throw new IOException("Process birth identity does not match; refusing to control another process.");
        }
        catch { process.Dispose(); throw; }
    }
}
