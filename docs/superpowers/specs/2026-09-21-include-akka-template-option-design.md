# `--include-akka`: Akka.NET actors and SignalR as a template option

Date: 2026-09-21
Status: Approved design, awaiting implementation plan

## Goal

Add an opt-in `--include-akka` option to the `publicotel-maui` `dotnet new` template. When
passed, the generated app gains an Akka.NET actor system behind the API, a message envelope
that carries `ActivityContext` so distributed traces survive the actor mailbox, and a SignalR
hub that pushes actor state changes live to the MAUI client.

The option defaults to **off**. An app generated without it matches what the template
produces today, apart from the two unconditional changes listed at the end of section 5.

## Why

This template is the reference solution for SE 3630 (Mobile) week 5. Two sources define the
content:

- `3630_Mobile/2026-fall-jonathan/Modules/00 - lectures/week-05/1-Monday.md` — "Learn Day:
  Aspire and Akka.NET". The actor contract (private state + mailbox + one message at a
  time), actor-per-entity, supervision as hierarchy, Akka.Hosting with the actor system in
  DI, `Tell` vs `Ask`, messages as immutable records. The lecture closes by deliberately
  planting a broken trace: auto-instrumentation carries an HTTP hop, but not a mailbox.
- `.../week-05/3-Wednesday.md` — "Tracing the Tap, SignalR". The fix: embed `ActivityContext`
  in the message record and start the child activity from it. Plus SignalR, hub on the API
  and `HubConnection` in MAUI.

The W05 drill (`Modules/01 Bootcamp/assignments/W05 Drill - Actors and Aspire.md`) grades all
three: an Aspire AppHost modelling the topology, a domain actor with real state and at least
two message types reached via `Ask`/`Tell`, and a SignalR update that reaches the MAUI client
without a refresh.

There is no Akka code in the faculty repo to copy. `demos/InstrumentThis` is the in-class
demo to be grown live and does not have it yet. This design is authored from the lecture.

## Non-goals

- The Blazor `PublicOtel.Web` frontend does not get live updates. The drill targets MAUI.
- No Akka.Cluster, Akka.Persistence, or Akka.Remote. Single-node, in-memory state.
- No new Aspire resources in `AppHost.cs`. SignalR rides the API service that already exists.
- No changes to the existing weather demo. `WeatherViewModel`, `WeatherApiClient`,
  `Telemetry.cs`, `MainPage.xaml` and their tests are untouched in both flag states. The only
  pre-existing files this design changes regardless of the flag are `AppShell.xaml.cs` and the
  `xunit.v3` pin in `PublicOtel.ClientTests.csproj` (section 5).

## Hard constraint: no `.sln` changes

The dotnet templating engine has no clean conditional syntax for `.sln` files, which admit no
comments. Therefore **the option adds no new projects**. Every file it introduces lands
inside a project that already exists. This constraint drives several decisions below,
notably putting the realtime UI on a second page rather than in a separate project.

## 1. The flag

### `template.json`

A new symbol:

```json
"IncludeAkka": {
  "type": "parameter",
  "datatype": "bool",
  "defaultValue": "false",
  "displayName": "Include Akka.NET actors and SignalR",
  "description": "Adds an Akka.NET actor system to the API service via Akka.Hosting, a message envelope carrying ActivityContext so traces survive the mailbox, and a SignalR hub that pushes actor state changes to the MAUI client."
}
```

This is the template's first boolean symbol; every existing symbol is a string, port,
derived or generated value.

### `dotnetcli.host.json`

```json
"IncludeAkka": { "longName": "include-akka", "shortName": "" }
```

Without this the CLI would expose the symbol as `--IncludeAkka`.

### `ide.host.json`

A `symbolInfo` entry with `"isVisible": true` so the option appears as a checkbox in the
Visual Studio 2022 New Project dialog, alongside the existing `applicationIdPrefix` entry.

### Content exclusion

Whole new directories and files are dropped when the flag is off, via a new entry in
`sources[0].modifiers`:

```json
{
  "condition": "(!IncludeAkka)",
  "exclude": [
    "PublicOtel.ApiService/Actors/**",
    "PublicOtel.ApiService/Hubs/**",
    "PublicOtel.ApiService/Telemetry/**",
    "PublicOtel.ClientLogic/Realtime/**",
    "PublicOtel.Mobile/StationsPage.xaml",
    "PublicOtel.Mobile/StationsPage.xaml.cs",
    "PublicOtel.ClientTests/Actors/**",
    "PublicOtel.ClientTests/Realtime/**",
    "PublicOtel.ClientTests/Features/Stations.feature",
    "PublicOtel.ClientTests/Steps/StationSteps.cs"
  ]
}
```

Files that already exist and need conditional fragments use `//#if (IncludeAkka)` /
`//#endif` in C#, and `<!--#if (IncludeAkka) -->` / `<!--#endif -->` in `.csproj` and
`.xaml`. The complete list of such files is in section 5.

## 2. API side — `PublicOtel.ApiService`

### Telemetry

`Telemetry/ApiTelemetry.cs` holds a static `ActivitySource` named literally
`PublicOtel.ApiService`. That string equals `builder.Environment.ApplicationName`, which
`PublicOtel.ServiceDefaults` already passes to `tracing.AddSource(...)`. So the source needs
no additional OpenTelemetry registration, and the template's `sourceName` substitution
renames it along with everything else. The file carries a comment saying exactly this,
because the coupling is otherwise invisible.

### Messages

`Actors/WeatherMessages.cs`, all `sealed record`:

- `ReportReading(string Station, int TemperatureC, ActivityContext TraceContext)`
- `GetLatestReading(string Station, ActivityContext TraceContext)`
- `StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt)` — reply and
  broadcast payload
- `NoReadingYet(string Station)` — reply

Every inbound message carries `ActivityContext`. Records, not classes: the sender keeps a
reference after `Tell`, so a mutable message is shared state with extra steps. The file says
so in a comment.

### `Actors/WeatherStationActor.cs`

A `ReceiveActor`, one instance per station id, holding private `StationReading? _latest`.

- `ReportReading` → validates, replaces `_latest`, broadcasts the new reading through an
  injected `IHubContext<WeatherHub, IWeatherClient>`. Reached by `Tell` from the API.
- `GetLatestReading` → replies with `StationReading` or `NoReadingYet`. Reached by `Ask`.

Each handler opens a child activity with
`ApiTelemetry.Source.StartActivity(name, ActivityKind.Consumer, parentContext: msg.TraceContext)`.
Directly above that argument sits a comment stating that deleting `parentContext:`
reproduces the orphan span from the Monday lecture — so an instructor can break the trace
live in a few seconds for Wednesday's kata without the template ever shipping broken.

A reading outside a plausible range throws `InvalidReadingException`, which exists to give
the supervisor something real to handle.

### `Actors/StationSupervisor.cs`

Actor-per-entity router. On each message it resolves the child for the station id via
`Context.Child(name)`, creating it if `Nobody`. Declares a `OneForOneStrategy`: `Restart` on
`InvalidReadingException`, `Escalate` otherwise. A comment states what restart costs — the
child's `_latest` is gone, because restart constructs a fresh instance — which is the
supervision half of the drill's explanation rubric.

### `Hubs/WeatherHub.cs`

`Hub<IWeatherClient>` mapped at `/hubs/weather`, with
`IWeatherClient.ReadingReported(StationReading reading)`.

### `Actors/WeatherStationEndpoints.cs`

Two endpoints on an extension method:

- `POST /stations/{id}/readings` — `Tell`s the supervisor, returns `202 Accepted`.
- `GET /stations/{id}` — `Ask`s the supervisor with an explicit timeout, returns `200` or
  `404`.

Both capture `Activity.Current?.Context ?? default` into the message. The `Ask` endpoint
carries a comment naming what `Ask` costs versus `Tell`: a timeout you must choose, a
continuation waiting, and an `AskTimeoutException` failure mode that `Tell` does not have.

### `Actors/WeatherActorExtensions.cs`

`AddWeatherActors(this IHostApplicationBuilder builder)` wrapping the `AddAkka(...)`
registration, so `Program.cs` takes one line rather than ten.

### Conditional edits

`Program.cs` gains a `//#if (IncludeAkka)` block of four lines: `AddSignalR()`,
`AddWeatherActors()`, `MapHub<WeatherHub>("/hubs/weather")`, `MapWeatherStationEndpoints()`.

`PublicOtel.ApiService.csproj` gains a conditional `PackageReference` to `Akka.Hosting`
version `1.5.71`. SignalR server-side needs no package; it ships in the web SDK.

## 3. Client side

### `PublicOtel.ClientLogic/Realtime/`

- `IStationApiClient` / `StationApiClient` — `POST /stations/{id}/readings` and
  `GET /stations/{id}`, following the shape of the existing `WeatherApiClient`.
- `IStationHubClient` / `StationHubClient` — wraps a `HubConnection`, exposes an event for
  inbound `StationReading` values. On reconnect it re-fetches current state and then resumes
  the stream, which is the ordering the Wednesday lecture calls out.
- `StationsViewModel` — `ObservableObject` with a `ReportReadingCommand`, a `LatestReading`
  property and an observable log of inbound pushes.

A separate view model in a separate directory is deliberate: it keeps `WeatherViewModel.cs`
free of conditional fragments. `WeatherViewModel` uses a primary constructor, so a new
dependency could not have been added from a second partial file.

`PublicOtel.ClientLogic.csproj` gains a conditional `PackageReference` to
`Microsoft.AspNetCore.SignalR.Client` version `10.0.11`, matching the repo's existing
`Microsoft.AspNetCore.OpenApi` pin.

### `PublicOtel.Mobile/StationsPage.xaml(.cs)`

A second page: a station picker, a **Report Reading** button, a live latest-reading label and
a list of inbound pushes. Two devices on the same station is the demo — report on one, watch
the other change without a refresh.

### `AppShell` — resolve pages from the container

`AppShell.xaml` gains a conditional second `ShellContent`:

```xml
<ShellContent x:Name="HomeContent" Title="Weather" Route="MainPage" />

<!--#if (IncludeAkka) -->
<ShellContent x:Name="StationsContent" Title="Stations" Route="StationsPage" />
<!--#endif -->
```

`AppShell.xaml.cs` switches from constructor-injected pages to an injected
`IServiceProvider`:

```csharp
public AppShell(IServiceProvider services)
{
    InitializeComponent();

    HomeContent.Content = services.GetRequiredService<MainPage>();
//#if (IncludeAkka)
    StationsContent.Content = services.GetRequiredService<StationsPage>();
//#endif
}
```

The alternative — a conditional constructor parameter — was rejected: the templating engine
is line-based, so with the flag off it emits a dangling `)` on its own line in a file meant
to be read as exemplary code. With `IServiceProvider` both generated variants are clean and
differ by exactly one line.

This is service location rather than constructor injection, which is acceptable here
specifically: `AppShell` is the composition root whose only job is assembling pages, and the
failure mode is unchanged, since MAUI resolves `AppShell` itself at runtime either way.

**This change applies unconditionally**, in both flag states and in the working solution. It
is the one way a flag-off generated app differs from today's output.

### `MauiProgram.cs`

A conditional block registering `IStationApiClient` (typed `HttpClient` against
`https+http://apiservice`, matching the existing registration), `IStationHubClient`,
`StationsViewModel` and `StationsPage`.

## 4. Repo layout and maintenance

The template content under `PublicOtel.Templates/templates/publicotel-aspire-maui/` is a
hand-maintained copy of the root solution. The decision:

- **The root solution gets Akka and SignalR always on.** It compiles, runs and is demoable
  from this repo, which is what makes the feature maintainable.
- **The template copy carries the same code wrapped in conditional markers.**
- **CI generates the app both ways** so neither path rots silently.

The consequence is that the root solution is no longer a faithful picture of the
`--include-akka false` output. CI's flag-off generated app is. That trade is accepted in
exchange for the Akka code being compiled and tested here rather than only in CI.

The Templates README already documents placeholders that deliberately differ between the
working copy and the template copy. That list gains one entry: the `//#if (IncludeAkka)` and
`<!--#if (IncludeAkka) -->` markers, which exist only in the template copy.

## 5. Complete list of conditional edits to existing files

| File | Marker style | What is conditional |
|---|---|---|
| `PublicOtel.ApiService/PublicOtel.ApiService.csproj` | XML comment | `Akka.Hosting` package reference |
| `PublicOtel.ApiService/Program.cs` | `//#if` | Four wiring lines |
| `PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj` | XML comment | `Microsoft.AspNetCore.SignalR.Client` package reference |
| `PublicOtel.Mobile/MauiProgram.cs` | `//#if` | Four registrations |
| `PublicOtel.Mobile/AppShell.xaml` | XML comment | Second `ShellContent` |
| `PublicOtel.Mobile/AppShell.xaml.cs` | `//#if` | One `GetRequiredService` line |
| `PublicOtel.ClientTests/PublicOtel.ClientTests.csproj` | XML comment | `Akka.TestKit.Xunit` package reference and `ProjectReference` to `PublicOtel.ApiService` |

Everything else the option adds is a whole file excluded by `sources.modifiers`.

Two pre-existing files also change **unconditionally**, in both flag states:
`PublicOtel.Mobile/AppShell.xaml.cs` (constructor takes `IServiceProvider`, section 3) and
`PublicOtel.ClientTests/PublicOtel.ClientTests.csproj` (`xunit.v3` 3.0.1 → 3.2.2, section 6).

## 6. Tests

### Client tests — `PublicOtel.ClientTests/Realtime/StationsViewModelTests.cs`

With `IStationApiClient` and `IStationHubClient` substituted via NSubstitute: an inbound push
updates `LatestReading` and appends to the log; the report command calls the API with the
selected station. Plus `Features/Stations.feature` and `Steps/StationSteps.cs` following the
existing Reqnroll convention, covering "a reading reported by one client reaches another".

These are plain `net10.0` and run in CI, like the existing client tests.

### Actor tests

Use **`Akka.TestKit.Xunit` 1.5.71**, which despite the name is the *xUnit v3* TestKit: at
1.5.71 it targets `net10.0` and depends on `xunit.v3.assert` and `xunit.v3.extensibility.core`
3.2.2. `Akka.TestKit.Xunit2` is the xUnit v2 package, and no `Akka.TestKit.Xunit3` exists.
Tests therefore derive from `TestKit` directly, in `PublicOtel.ClientTests`, behind a
conditional `ProjectReference` to `PublicOtel.ApiService`. No assertions shim is needed.

Referencing the API project from a test project is already the pattern `PublicOtel.Tests`
uses, and `PublicOtel.ApiService` is `net10.0`, so it still builds on the Linux CI runner.

**One unconditional prerequisite:** `PublicOtel.ClientTests` pins `xunit.v3` 3.0.1, and the
TestKit needs the 3.2.2 extensibility assemblies. That pin moves to `xunit.v3` 3.2.2 in both
copies of the csproj, unconditionally — keeping one version in the file is simpler than making
the pin conditional, and the bump is low risk. `Reqnroll.xUnit.v3` 3.3.4 requires only
`>= 2.0.0` of the same assemblies and is satisfied by 3.2.2. `xunit.runner.visualstudio`
stays at 3.1.4; bumping it is not required and is out of scope.

Actors are constructed with `Props.Create` and a substituted
`IHubContext<WeatherHub, IWeatherClient>`, so these tests need no host.
`Akka.Hosting.TestKit` 1.5.71 is also on xUnit v3 and would let us exercise the real `AddAkka`
registration, but it pulls Akka.Persistence in and is not needed here.

Coverage: the actor keeps state across two messages, `Ask` returns `NoReadingYet` before any
report, an invalid reading triggers the supervisor's restart and the child's state is gone
afterwards, and the child activity created from a supplied `ActivityContext` has the expected
parent.

The only residual uncertainty is the `xunit.v3` bump coexisting with Reqnroll, so the
implementation plan validates that first — a restore and a green run of the existing tests
before any actor test is written.

## 7. CI — `.github/workflows/pack.yml`

The smoke-test step generates the app twice:

- `Ci.Smoke` with no flag, and `Ci.SmokeAkka` with `--include-akka`.
- The existing unsubstituted-token grep runs against both.
- `dotnet test` runs against both `ClientTests` projects.
- Additionally, `dotnet build` runs against `Ci.SmokeAkka`'s `ApiService`, which is `net10.0`
  and builds on Linux. This is what catches a broken actor or hub before release.

The existing constraints are unchanged: the MAUI project and `PublicOtel.Tests` still cannot
build on the runner, for the reasons the workflow already documents.

## 8. Documentation and versioning

- `PublicOtel.Templates/README.md`: a row for `--include-akka` in the options table; a new
  section describing what the flag adds, the endpoints, and how to run the two-device demo;
  the maintenance list gains the conditional-marker entry.
- Root `README.md`: the same summary, shorter.
- A documented caveat: SignalR to the Android emulator rides the existing `mobile-api` dev
  tunnel. Dev tunnels do carry WebSockets, but it is a new failure surface and the first
  thing to check if live updates do not arrive on Android while Windows works.
- `PublicOtel.Templates.csproj` `<Version>` moves from `1.8.0` to `1.9.0`, which is what
  causes the pack workflow to cut a release.

## 9. Acceptance criteria

1. `dotnet new publicotel-maui -n X` produces today's output, differing only in the two
   unconditional changes listed at the end of section 5: the `AppShell.xaml.cs` switch to
   `IServiceProvider` and the `xunit.v3` pin moving to 3.2.2.
2. `dotnet new publicotel-maui -n X --include-akka` produces a solution that builds, with no
   unsubstituted tokens and no stray `#if` markers in any generated file.
3. In the flag-on app, a `POST /stations/{id}/readings` from the MAUI client produces a
   single trace in the Aspire dashboard spanning phone, API, and actor, with no orphan spans.
4. Two running clients on the same station: a reading reported on one appears on the other
   without a refresh.
5. `--include-akka` appears in `dotnet new publicotel-maui --help` and as a checkbox in the
   Visual Studio New Project dialog.
6. CI passes for both generated variants.
