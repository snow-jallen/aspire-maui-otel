# PublicOtel.Templates

A `dotnet new` template pack for an Aspire solution with a .NET MAUI client wired for
OpenTelemetry.

## What the template generates

| Project | Purpose |
|---|---|
| `<Name>.AppHost` | Aspire AppHost. Wires the API, web frontend, and MAUI Windows/Android targets, plus dev tunnels for mobile. |
| `<Name>.ApiService` | Minimal API serving `/weatherforecast`. |
| `<Name>.Web` | Blazor frontend. |
| `<Name>.Mobile` | .NET MAUI app. One **Get Weather** button that calls the API and renders the results. |
| `<Name>.ServiceDefaults` | Shared Aspire defaults for the ASP.NET Core projects. |
| `<Name>.MauiServiceDefaults` | Aspire defaults adapted for MAUI — including the environment-variable configuration source that `MauiApp.CreateBuilder()` does not add on its own. |
| `<Name>.ClientLogic` | UI-agnostic client logic: the API client, the telemetry instruments, and a `WeatherViewModel` built on **CommunityToolkit.Mvvm**. Referenced by `<Name>.Mobile`. |
| `<Name>.ClientTests` | Unit and BDD tests for `ClientLogic`, using **NSubstitute**, **Shouldly**, and **Reqnroll** (xUnit v3). |
| `<Name>.Tests` | xUnit integration tests against the AppHost. |

The page heading shows the name the app was generated with, so `dotnet new publicotel-maui -n Demo5` produces an app titled **Demo5**.

Pressing the button produces a single distributed trace spanning the mobile app and the API
service, structured logs, and a custom meter, all visible in the Aspire dashboard.

## Running it

```bash
aspire start
```

Then in the dashboard, start the platform you want.

**Windows** works directly: start the `mobile-windows` resource, press **Get Weather**, and
the trace appears under Traces.

**Android**: on the `mobile-android-emulator` resource, use its **▶ Run on Android**
command — *not* Start. Start always fails with NETSDK1085 because of a bug in
`Aspire.Hosting.Maui`, still present in 13.5.4-preview.1, the version this template ships
(see below). The command does the whole thing in one step and is the only action you need.
If someone presses Start anyway, the resource's console log says so twice: once as Start
begins, and again directly under the NETSDK1085 error, pointing at the command.

Equivalently, from a terminal at the repo root:

```powershell
./scripts/run-android.ps1
```

You will see the NETSDK1085 failure scroll past partway through. **That is expected** — the
script triggers that build deliberately to produce the environment file, then launches the
app itself. Success looks like `Running on Android emulator.` at the end.

### If Android telemetry does not appear

Android reaches the dashboard through **dev tunnels**, which need a Microsoft login the
first time. Two things bite:

**1. There is a hard cap of 10 tunnels per account.** Every app generated from this template
creates **two** (`mobile` and `mobile-api`), with IDs derived from the folder path — so a new
folder means two more tunnels. Five apps fills the account. Over the cap, the service
reports it confusingly as a rate limit:

```
Rate limit exceeded. Please wait about a minute and try again.
```

Check and clean up with the `devtunnel` CLI:

```bash
devtunnel list                 # if this says "Found 10 tunnels", you are at the cap
devtunnel delete <tunnel-id>   # tunnels are recreated automatically on the next run
```

**2. Genuine rate limiting** on rapid restarts, which does clear after about a minute.

In both cases the tunnel resources show `FailedToStart`, and `run-android.ps1` stops with an
explanation rather than launching an app that silently reports nothing.

The **Windows** target uses no tunnels at all — no login, no cap, no rate limit. **In a
classroom, demo Windows first and treat Android as the stretch goal.** If every student
generates their own app, they will each need their own dev tunnel login, and anyone reusing
a shared account will hit the 10-tunnel cap quickly.

## The Android bug, in one paragraph

The MAUI integration pre-builds the project, then asks DCP to launch it with
`-p:NoBuild=true`. On Windows that is harmless, because `/t:Run` resolves to the SDK's `Run`
target, which never reaches `Build`. On Android, `Run` depends on `Install`, which pulls in
the full build chain, so `Build` **is** invoked and the SDK's `_CheckForBuildWithNoBuild`
guard hard-errors. The flag arrives as an MSBuild global property and the guard is defined
after any user `Directory.Build.targets`, so it cannot be overridden from the repo. The
workaround simply runs the same command without that one flag. A physical device
(`AddAndroidDevice`) fails identically — the error is in the MSBuild target chain, before
adb target selection matters. Remove `scripts/run-android.ps1` and the `WithCommand` block
in `AppHost.cs` once the integration is fixed upstream.

Because the failure is in the MSBuild target chain, you can re-check it after any
`Aspire.Hosting.Maui` bump without an emulator — this is the exact command the integration
issues, and it fails in seconds:

```bash
dotnet build <Name>.Mobile/<Name>.Mobile.csproj -f net10.0-android --no-restore -t:Run -p:NoBuild=true
```

`error NETSDK1085` means the bug is still there. The guard is
`_CheckForBuildWithNoBuild` in `Microsoft.NET.Sdk.targets`, which errors whenever `NoBuild`
is set; dropping `-p:NoBuild=true` from that same command passes it, which is all
`run-android.ps1` does. Verified still failing on 13.5.4-preview.1.

## Install

```bash
dotnet pack PublicOtel.Templates/PublicOtel.Templates.csproj -c Release
dotnet new install PublicOtel.Templates/bin/Release/PublicOtel.Templates.1.11.0.nupkg
```

Once installed the template appears in `dotnet new list` and in the Visual Studio 2022
(17.9+) New Project dialog.

## Use

```bash
dotnet new publicotel-maui -n Contoso.Telemetry --application-id-prefix com.contoso
```

| Option | Default | Effect |
|---|---|---|
| `-n, --name` | `AspireMauiApp` | Renames every project, namespace, and the solution. Also drives the lower-cased MAUI `ApplicationId`, the OpenTelemetry meter and instrument names, and the dashboard's `*.dev.localhost` host. |
| `--application-id-prefix` | `com.companyname` | Reverse-DNS prefix for the MAUI `ApplicationId`. |
| `--include-akka` | `false` | Adds an Akka.NET actor system to the API, a message envelope that carries `ActivityContext` across the actor mailbox, and a SignalR hub pushing live updates to a second page in the MAUI app. |

Project GUIDs, the solution GUID, the AppHost `UserSecretsId`, and all ten launch-profile
ports are regenerated per instantiation, so two apps from this template can be open and
running side by side.

## `--include-akka`

```bash
dotnet new publicotel-maui -n Contoso.Telemetry --include-akka
```

Adds three things that go together.

**An actor per weather station.** `StationSupervisor` creates one `WeatherStationActor` per
station id and supervises it. Each actor owns its station's latest reading in a private field
with no lock, because a mailbox delivers one message at a time. A reading outside
-90..60°C throws `InvalidReadingException`, and the supervisor's `OneForOneStrategy` restarts
that child — which discards its state, the cost restart actually has.

**A trace that survives the mailbox.** Auto-instrumentation propagates trace context across an
HTTP hop through the `traceparent` header. Nothing propagates it across a mailbox: `Tell`
returns immediately and the actor runs later, on another thread, with no ambient `Activity`.
So every message record carries an `ActivityContext`, and the actor starts its span with
`parentContext:` set from it. Delete that one argument in `WeatherStationActor` and the actor
span becomes a parentless root floating beside the API span — the standard demonstration of
why this matters, and it takes five seconds to stage.

**Logs that belong to the trace.** The actor logs through an injected `ILogger`, not Akka's
own `ILoggingAdapter`. The difference matters: `ILogger` writes synchronously, on the actor's
thread, while the actor span is `Activity.Current`, so each record carries that span's trace
id and appears inside the request's trace in the dashboard. Akka's adapter hands the message
to a logging actor that writes it later, on another thread, with no current `Activity`. Akka's
own output — supervision restarts, dead letters — is still routed into the dashboard's
structured logs by `ConfigureLoggers(... AddLoggerFactory())` in `WeatherActorExtensions`, but
those records belong to no trace. Report an out-of-range reading and you can see both: the
actor's warning inside the trace, and Akka's restart error beside it with no trace id.

**SignalR.** `WeatherHub` at `/hubs/weather` broadcasts on every state change; the MAUI app's
**Stations** tab subscribes and updates live. Alongside the station and temperature fields,
the **Connect** and **Report Reading** buttons, and the list of pushed updates, the page shows
a live `Latest: <station> <temp>°C` label bound to the most recent reading. Reports go over
HTTP rather than a hub method, deliberately: an HTTP POST carries `traceparent` and starts
inside a trace, a hub invocation does not.

| Endpoint | Actor call | Response |
|---|---|---|
| `POST /stations/{station}/readings` | `Tell` | `202 Accepted` — the actor may not have processed it yet |
| `GET /stations/{station}` | `Ask` (3s timeout) | `200` with the reading, or `404` |

### The two-device demo

Start the app, open the **Stations** tab on two devices (two emulators, or Windows plus an
emulator), press **Connect** on both, then **Report Reading** on one. The other updates with
no refresh.

Android reaches the API over the existing `mobile-api` dev tunnel, and SignalR therefore rides
that tunnel too. Dev tunnels do carry WebSockets, but it is a new failure surface: if live
updates work on Windows and not on Android, check the tunnel before suspecting the hub.

## Uninstall

```bash
dotnet new uninstall PublicOtel.Templates
```

## Maintaining the template

The template content under `templates/publicotel-aspire-maui/` is a copy of the source
solution. When you change the real solution and want those changes in the template, copy the
files across and then re-apply the placeholders that deliberately differ from working
code:

- `Projects.GeneratedClassNamePrefix_*` in `AppHost.cs` and `WebTests.cs` — the Aspire source
  generator replaces dots with underscores, so `sourceName` substitution alone cannot produce
  a valid identifier.
- The placeholder ports in the three `launchSettings.json` files (`15000`, `17000`, `19000`,
  `20000`, `21000`, `22000`, `5000`, `7000`, `5301`, `7301`), which the port symbols match on.
- `<ApplicationId>com.companyname.APPIDNAME.mobile</ApplicationId>` in the Mobile csproj — the working copy holds a literal name; the template holds the token.
- `tunnelId: "mobile-api-TUNNELSUFFIX"` in `AppHost.cs` — the working copy holds a real
  number; the template holds the token, which the `tunnelSuffix` symbol replaces with a
  random six-digit value per generated app.
- `private const string ActorSystemName = "ACTORSYSTEMNAME";` in
  `PublicOtel.ApiService/Actors/WeatherActorExtensions.cs` — the working copy holds
  `"publicotel"`, a valid name that keeps the real solution runnable; the template holds the
  token, which the `actorSystemName` symbol replaces with the project name lower-cased and
  stripped of separators. `lowerCaseName` cannot be used here: Akka accepts only
  `[a-zA-Z0-9]` plus a non-leading `-`, so `-n Contoso.Telemetry` would produce
  `contoso.telemetry` and the generated ApiService would throw `ArgumentException: Invalid
  ActorSystem name` before it finished starting. This is the one file the branch added whose
  two copies are not byte-identical.
- The `//#if (IncludeAkka)` and `<!--#if (IncludeAkka) -->` markers — 14 marker pairs across
  seven files: `Program.cs`, `MauiProgram.cs`, `AppShell.xaml`, `AppShell.xaml.cs`, and the
  `PublicOtel.ApiService`, `PublicOtel.ClientLogic`, and `PublicOtel.ClientTests` `.csproj`
  files. The working copy has that code unconditionally; only the template copy carries
  markers. Whole files need no markers — the `(!IncludeAkka)` modifier in `template.json`
  excludes all ten paths by name when the flag is off: the directories
  `PublicOtel.ApiService/Actors/`, `PublicOtel.ApiService/Hubs/`,
  `PublicOtel.ApiService/Telemetry/`, `PublicOtel.ClientLogic/Realtime/`,
  `PublicOtel.ClientTests/Actors/` and `PublicOtel.ClientTests/Realtime/`, and the four
  individual files `PublicOtel.ClientTests/Features/Stations.feature`,
  `PublicOtel.ClientTests/Steps/StationSteps.cs`, `PublicOtel.Mobile/StationsPage.xaml` and
  `PublicOtel.Mobile/StationsPage.xaml.cs`. Add a new unconditional Akka-only file and it
  needs a new entry there. One asymmetry worth preserving in
  `PublicOtel.ClientTests.csproj`: the `xunit.v3` 3.2.2 pin is unconditional
  (both variants need it, per the comment beside the `PackageReference`), while only
  `Akka.TestKit.Xunit` and the `ProjectReference` to `PublicOtel.ApiService` are wrapped in
  `IncludeAkka` markers. It is easy to "tidy" that pin into the conditional block by mistake —
  don't.

## Dev tunnel IDs

Each generated app pins its API tunnel to `mobile-api-<random six digits>`, chosen once at
generation time. Left unpinned, Aspire derives the ID from a hash of the AppHost folder, so
moving or renaming the folder silently orphans the old tunnel — and orphans still count
against the cap for 20–30 days.

Two caveats:

- **The OTLP tunnel cannot be pinned.** `WithOtlpDevTunnel()` takes no `tunnelId`
  parameter, so that one is still `mobile-<folder hash>`.
- **Pinning does not raise the 10-tunnel cap.** Each app still needs two tunnels. Generating
  a *new* app still draws a new random suffix and therefore a new tunnel.

## Android ApplicationId and Java keywords

The `ApplicationId` is built as `<prefix>.<project name, lower-cased, separators removed>.mobile`
— note **separators removed**, not just lower-cased. Each dot starts a new Java package
segment, and the Android build generates an `R.java` containing `package <ApplicationId>;`.
A segment that happens to be a Java reserved word will not compile, and the error is
unhelpful:

```
R.java:8: error: <identifier> expected
package com.companyname.final.check.mobile;
                        ^
```

Joining the name into one segment avoids this for multi-word names — `Final.Check` becomes
`com.companyname.finalcheck.mobile`. A single-word project named exactly after a Java
keyword (`Final`, `New`, `Class`, `Public`, `Static`) would still collide; set
`--application-id-prefix` or rename the project if you hit it.

## Akka test project names and `TestKit`

`WeatherStationActorTests` and `StationSupervisorTests` (generated with `--include-akka`)
deliberately write their base class as the fully-qualified `Akka.TestKit.Xunit.TestKit`
rather than a bare `TestKit`. A bare `TestKit` fails to compile with `CS0118` in any generated
app whose name starts with `Akka.` — C# resolves the bare name to the sibling `Akka.TestKit`
namespace instead of the class. CI guards against a regression here by generating its
flag-on smoke app as `Akka.CiSmoke`, which would trip exactly this collision if the base
class were ever changed back to a bare `TestKit`.
