using Bomber.Client.Updates;

if (args.Length != 1) throw new ArgumentException("Expected one private update request path.");
try { await DesktopUpdateTransaction.Run(args[0], CancellationToken.None); }
catch (Exception error)
{
    // This detached helper writes diagnostics beside its private transaction, never to a public receipt.
    await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "helper-error.txt"), error.ToString());
    Environment.ExitCode = 1;
}
