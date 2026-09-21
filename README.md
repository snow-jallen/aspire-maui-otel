# aspire-maui-otel

The **PublicOtel** solution: an [Aspire](https://aspire.dev) solution that shows **OpenTelemetry from a .NET MAUI client**
— one button in a mobile app producing a single distributed trace across the app and the
backing API, visible in the Aspire dashboard.

It ships two things:

1. A working solution you can run.
2. A `dotnet new` **template pack** that generates the same solution under any name, for
   handing out to a class.

## The solution

| Project | Purpose |
|---|---|
| `PublicOtel.AppHost` | Aspire AppHost. Wires the API, the web frontend, and MAUI Windows/Android targets, plus dev tunnels for mobile. |
| `PublicOtel.ApiService` | Minimal API serving `/weatherforecast`. |
| `PublicOtel.Web` | Blazor frontend. |
| `PublicOtel.Mobile` | .NET MAUI app. One **Get Weather** button. |
| `PublicOtel.ClientLogic` | UI-agnostic client logic: API client, telemetry instruments, and a `WeatherViewModel` on CommunityToolkit.Mvvm. |
| `PublicOtel.ClientTests` | Unit and BDD tests for `ClientLogic` — NSubstitute, Shouldly, Reqnroll, xUnit v3. |
| `PublicOtel.ServiceDefaults` | Shared Aspire defaults for the ASP.NET Core projects. |
| `PublicOtel.MauiServiceDefaults` | Aspire defaults adapted for MAUI. |
| `PublicOtel.Tests` | Integration tests against the AppHost. |
| `PublicOtel.Templates` | The `dotnet new` template pack. |

## Running it

```bash
aspire start
```

Then start the **`mobile-windows`** resource from the dashboard and press **Get Weather**.
You should see one trace spanning `mobile-windows` → `apiservice`, structured logs, and a
custom meter.

For **Android**, use the `mobile-android-emulator` resource's **▶ Run on Android** command
rather than Start — see [the template README](PublicOtel.Templates/README.md) for why, and
for the dev tunnel caveats.

## Akka.NET and SignalR

`dotnet new publicotel-maui -n Your.App --include-akka` adds an Akka.NET actor system behind
the API, a message envelope that carries `ActivityContext` so a trace survives the actor
mailbox, and a SignalR hub that pushes state changes to a live **Stations** page in the MAUI
app. See [the template README](PublicOtel.Templates/README.md) for what it generates and how
to run the two-device demo.

## Two things worth knowing

**MAUI configuration has no environment-variable source.** `MauiApp.CreateBuilder()` hands
you an empty `ConfigurationManager`, unlike the ASP.NET Core host. Without
`AddEnvironmentVariables()`, the OTLP endpoint and the service discovery keys that Aspire
injects are invisible to the app, so telemetry silently never arrives. That one line lives in
`PublicOtel.MauiServiceDefaults/Extensions.cs`.

**Starting the Android resource always fails with NETSDK1085.** `Aspire.Hosting.Maui`
13.5.3-preview.1 launches Android with `-p:NoBuild=true`, but Android's `Run` target depends
on `Install`, which invokes `Build`. `scripts/run-android.ps1` runs the same command without
that flag. Details in the template README.

## Building the template pack

```bash
dotnet pack PublicOtel.Templates/PublicOtel.Templates.csproj -c Release
dotnet new install PublicOtel.Templates/bin/Release/PublicOtel.Templates.<version>.nupkg
dotnet new publicotel-maui -n Your.AppName
```

`PublicOtel.Templates/templates/publicotel-aspire-maui/` is a copy of this solution with a
handful of tokens substituted at generation time. Keeping the two in step is described under
*Maintaining the template* in [the template README](PublicOtel.Templates/README.md).

## Requirements

- .NET 10 SDK
- MAUI workload: `dotnet workload install maui`
- Aspire CLI: `dotnet tool install -g Aspire.Cli`
- For Android: an emulator, and a dev tunnel login
