using System.Diagnostics;

/// <summary>Port-scoped loss on a disposable Linux runner. Never shapes a production host.</summary>
internal sealed class NetemFault(int port) : IAsyncDisposable
{
    private bool _owned;
    private static async Task<string> Command(params string[] arguments)
    {
        var start = new ProcessStartInfo("sudo") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-n"); start.ArgumentList.Add("tc");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(); throw; }
        string text = await output, problem = await error;
        if (process.ExitCode != 0) throw new IOException("Owned netem fixture failed: " + problem);
        return text;
    }
    public async Task Apply(bool upstream, bool downstream, bool jitter)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Netem is restricted to a disposable hosted Linux test runner.");
        string before = await Command("qdisc", "show", "dev", "lo");
        if (!before.Contains("qdisc noqueue 0:", StringComparison.Ordinal) || before.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 1)
            throw new InvalidOperationException("Refusing to replace an existing loopback qdisc.");
        await Command("qdisc", "add", "dev", "lo", "root", "handle", "7b00:", "prio", "bands", "3"); _owned = true;
        string[] netem = jitter ? ["delay", "550ms", "250ms"] : ["loss", "100%"];
        await Command(["qdisc", "add", "dev", "lo", "parent", "7b00:3", "handle", "7b30:", "netem", .. netem]);
        if (upstream) await Command("filter", "add", "dev", "lo", "parent", "7b00:", "protocol", "ip", "pref", "21", "u32", "match", "ip", "dport", port.ToString(), "0xffff", "flowid", "7b00:3");
        if (downstream) await Command("filter", "add", "dev", "lo", "parent", "7b00:", "protocol", "ip", "pref", "22", "u32", "match", "ip", "sport", port.ToString(), "0xffff", "flowid", "7b00:3");
    }
    public async Task Restore()
    {
        if (_owned) await Command("qdisc", "change", "dev", "lo", "parent", "7b00:3", "handle", "7b30:", "netem", "loss", "0%");
    }
    public async ValueTask DisposeAsync()
    {
        if (!_owned) return;
        string current = await Command("qdisc", "show", "dev", "lo");
        if (!current.Contains("qdisc prio 7b00:", StringComparison.Ordinal))
            throw new InvalidOperationException("Owned netem root changed; refusing unrelated cleanup.");
        await Command("qdisc", "del", "dev", "lo", "root", "handle", "7b00:"); _owned = false;
    }
}
