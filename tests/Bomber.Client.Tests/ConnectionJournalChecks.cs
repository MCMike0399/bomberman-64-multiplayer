using System.Text.Json;
using Bomber.Client.Diagnostics;

internal static class ConnectionJournalChecks
{
    public static async Task<int> Run()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blast-journal-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        try
        {
            var journal = new ConnectionJournal(directory);
            for (int i = 0; i < 100; i++)
            {
                journal.Connection(i, "connected https://example.invalid/connect?token=private-value Authorization=private-header Bearer private-bearer Authorization: Bearer extra-private {\"token\":\"json-private\"}");
                journal.Rtt(i, 300 + i);
            }
            await journal.DisposeAsync();
            string text = File.ReadAllText(journal.Path);
            var entries = JsonSerializer.Deserialize<ConnectionJournal.Entry[]>(text)!;
            Check(entries.Count(x => x.Kind == "connection") == 64 && entries.Count(x => x.Kind == "rtt") == 64,
                "connection diagnostics retain only the last 64 events and 64 RTT spikes");
            Check(!text.Contains("private-value") && !text.Contains("private-header") && !text.Contains("private-bearer") && !text.Contains("extra-private") && !text.Contains("json-private"),
                "persisted diagnostics redact endpoints and credentials");
            Check(new FileInfo(journal.Path).Length <= 65536 && !journal.WriteFailed, "the persistent journal stays within its byte bound");
            var resumed = new ConnectionJournal(directory); resumed.Connection(101, "reconnected"); await resumed.DisposeAsync();
            var restored = JsonSerializer.Deserialize<ConnectionJournal.Entry[]>(File.ReadAllText(resumed.Path))!;
            Check(restored.Any(x => x.Generation == 101) && restored.Length <= 128, "history survives a session restart without growing");
            Check(File.Exists(resumed.Path + ".previous") && new FileInfo(resumed.Path + ".previous").Length <= 65536,
                "at most one bounded previous journal is rotated");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine($"PASS {checks} persisted connection journal checks");
        return checks;
    }
}
