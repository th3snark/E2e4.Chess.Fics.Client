# E2e4.Chess.Fics.Client

A cross-platform (`net10.0`) client library for the [Free Internet Chess Server](https://www.freechess.org) (FICS):
`System.IO.Pipelines` framing, a typed message parser, style-12 board parsing, an `IFicsTransport` seam over TCP and
an optional, pluggable timeseal codec. Install from NuGet: `E2e4.Chess.Fics.Client`.

This is an unofficial library. It is not affiliated with, or endorsed by, FICS or its administrators.

```csharp
await using IFicsClient client = new FicsClientFactory(Options.Create(new FicsClientOptions()), new TcpFicsTransport()).Create();
client.BoardUpdated += (_, e) => Console.WriteLine(e.Board);
await client.ConnectAsync();
string user = await client.LoginAsGuestAsync();
await client.SendCommandAsync("seek 5 0");
```

With dependency injection, call `services.AddFicsClient(options => ...)` and inject `IFicsClientFactory`. A client is a single
use session; long-lived services should inject the factory and dispose each client with `DisposeAsync`.

## Licence

MIT, see [LICENSE](https://github.com/th3snark/E2e4.Chess.Fics.Client/blob/master/LICENSE). The library has no dependency on any GPL
code. Timeseal is optional and plugs in through `ITimesealCodec` (`FicsClientOptions.Timeseal`); leave it null to send plain lines.
The test fixtures are synthetic; see [NOTICE](https://github.com/th3snark/E2e4.Chess.Fics.Client/blob/master/NOTICE).

### Using timeseal

The timeseal cipher is GPL v2 or later and lives in a separate package,
[E2e4.Chess.OpenSeal](https://github.com/th3snark/E2e4.Chess.OpenSeal). **An application that includes that package is distributed under
the GPL.** If you accept that, the adapter is one line:

```csharp
sealed class OpenSealCodec : ITimesealCodec
{
    public string Encode(string line) => OpenSealCipher.Encode(line);
}
```

Do not reference `E2e4.Chess.OpenSeal` from a library you want to keep permissively licensed.

## Privacy and security notes

- FICS speaks plain, unencrypted Telnet. A password sent with `LoginAsync` and everything you chat crosses the network in the
  clear, and this library cannot change that. Prefer guest logins for demos, and never reuse a password from elsewhere.
- Outbound lines are checked: a command, user name, password or interface name containing `\r`, `\n` or `\0` is rejected, so a
  value cannot smuggle in a second command.
- Inbound lines and frames have length caps, and the parser turns anything it does not understand into `UnknownLineMessage`
  instead of throwing.
- The optional `ILogger` receives state changes and swallowed failures, never raw lines or passwords.
- `FicsClientOptions.Host` defaults to `freechess.org`; point it elsewhere for another server.

## Build and test

```
dotnet test FicsClient.slnx
```

See [SECURITY.md](https://github.com/th3snark/E2e4.Chess.Fics.Client/blob/master/SECURITY.md) to report a vulnerability.
