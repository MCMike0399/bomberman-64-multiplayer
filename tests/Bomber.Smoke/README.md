# Network smoke checks

Start the real host with `dotnet run --project src/Bomber.Server`, then run:

```sh
dotnet run --project tests/Bomber.Smoke -- http://127.0.0.1:18496
```

The executable uses guest HTTP authentication and actual WebSockets. It checks the product LocalSession and NetworkSession adapters, private code joining, remote movement and bomb input, separate 2–8-human lobbies with matching rosters, input sequence acknowledgements, abrupt disconnect and fresh-reservation reconnect, and one human with seven moving server bots. It needs no external test framework. Nonzero exit indicates a failed check.

Quick play also checks cancellation of a waiting product client, then matches two actual product clients into the same authoritative lobby. Pass `--quick-only` to run just this check.

Pass `--client-only` to verify just the product session adapters. Supply a different origin as the positional argument to test another running host.
