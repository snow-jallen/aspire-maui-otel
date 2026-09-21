# `--include-akka` Template Option Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in `--include-akka` option to the `publicotel-maui` `dotnet new` template that generates an Akka.NET actor system behind the API, a message envelope carrying `ActivityContext` so traces survive the actor mailbox, and a SignalR hub pushing actor state changes live to the MAUI client.

**Architecture:** Build the feature in the root working solution first, where it compiles and is testable, then port it to the hand-maintained template copy wrapped in `//#if (IncludeAkka)` markers. No new projects are created, because the templating engine has no clean conditional syntax for `.sln` files — every new file lands inside a project that already exists. The realtime demo is a second MAUI page so the existing weather demo stays untouched.

**Tech Stack:** .NET 10, Aspire 13.5, .NET MAUI, Akka.NET 1.5.71 (Akka.Hosting), ASP.NET Core SignalR, OpenTelemetry, xUnit v3, NSubstitute, Shouldly, Reqnroll.

**Spec:** `docs/superpowers/specs/2026-09-21-include-akka-template-option-design.md`

## Global Constraints

- **Package versions, exact:** `Akka.Hosting` 1.5.71 · `Akka.TestKit.Xunit` 1.5.71 (this is the **xUnit v3** TestKit; `Akka.TestKit.Xunit2` is the v2 one) · `Microsoft.AspNetCore.SignalR.Client` 10.0.11 · `Microsoft.Extensions.Configuration.Abstractions` 10.0.11 · `xunit.v3` 3.2.2 (bumped from 3.0.1).
- **Do not bump** `xunit.runner.visualstudio` (stays 3.1.4) or any other existing package. Out of scope.
- **No new projects and no `.sln` edits.** Ever. If a task seems to need one, stop and escalate.
- **Template token hazards.** In the template copy, `PublicOtel` is the `sourceName` and the bare lowercase string `publicotel` is replaced by the `lowerCaseName` symbol. Never introduce a lowercase `publicotel` literal you do not want rewritten per generated app. `APPIDNAME`, `TUNNELSUFFIX` and `GeneratedClassNamePrefix` are likewise live tokens.
- **Indentation follows the project you are in:** tabs in `PublicOtel.ClientLogic`, `PublicOtel.ClientTests`, `PublicOtel.Mobile`, `PublicOtel.MauiServiceDefaults`; four spaces in `PublicOtel.ApiService`, `PublicOtel.ServiceDefaults`, `PublicOtel.AppHost`.
- **Conditional markers (`//#if (IncludeAkka)`, `<!--#if (IncludeAkka) -->`) exist ONLY in the template copy** under `PublicOtel.Templates/templates/publicotel-aspire-maui/`. The root working solution has the code unconditionally.
- **Commit trailer.** End every commit message with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`
- **Work on a branch.** Task 1 Step 1 creates it. Do not commit to `main`.

---

## File Structure

### Created in the root working solution

| File | Responsibility |
|---|---|
| `PublicOtel.ApiService/Telemetry/ApiTelemetry.cs` | The API's `ActivitySource`, named to match what ServiceDefaults already registers. |
| `PublicOtel.ApiService/Actors/TraceEnvelope.cs` | Captures ambient trace context for putting inside a message. |
| `PublicOtel.ApiService/Actors/WeatherMessages.cs` | The four immutable message records. |
| `PublicOtel.ApiService/Actors/InvalidReadingException.cs` | The domain failure the supervisor decides about. |
| `PublicOtel.ApiService/Hubs/WeatherHub.cs` | SignalR hub and its strongly-typed client interface. |
| `PublicOtel.ApiService/Actors/WeatherStationActor.cs` | One actor per station; owns that station's latest reading. |
| `PublicOtel.ApiService/Actors/StationSupervisor.cs` | Actor-per-entity router plus the supervision strategy. |
| `PublicOtel.ApiService/Actors/WeatherActorExtensions.cs` | `AddWeatherActors()` — the Akka.Hosting registration. |
| `PublicOtel.ApiService/Actors/WeatherStationEndpoints.cs` | The two HTTP endpoints, one `Tell`, one `Ask`. |
| `PublicOtel.ClientLogic/Realtime/ApiServiceAddress.cs` | Resolves the API base address from Aspire's injected service-discovery keys. |
| `PublicOtel.ClientLogic/Realtime/StationApiClient.cs` | Typed HTTP client for the two station endpoints. |
| `PublicOtel.ClientLogic/Realtime/StationHubClient.cs` | Wraps `HubConnection`; raises events for inbound pushes. |
| `PublicOtel.ClientLogic/Realtime/StationsViewModel.cs` | Drives the stations page. No MAUI types. |
| `PublicOtel.Mobile/StationsPage.xaml(.cs)` | The second page. |
| `PublicOtel.ClientTests/Actors/*.cs` | Actor tests (TestKit). |
| `PublicOtel.ClientTests/Realtime/*.cs` | Client logic tests. |
| `PublicOtel.ClientTests/Features/Stations.feature`, `Steps/StationSteps.cs` | Reqnroll coverage of the realtime flow. |

### Modified in the root working solution

| File | Change |
|---|---|
| `PublicOtel.ApiService/PublicOtel.ApiService.csproj` | `Akka.Hosting` package reference. |
| `PublicOtel.ApiService/Program.cs` | Four wiring lines. |
| `PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj` | SignalR client + Configuration.Abstractions package references. |
| `PublicOtel.ClientTests/PublicOtel.ClientTests.csproj` | `xunit.v3` bump, `Akka.TestKit.Xunit`, `ProjectReference` to ApiService. |
| `PublicOtel.Mobile/MauiProgram.cs` | Four registrations. |
| `PublicOtel.Mobile/AppShell.xaml` | Second `ShellContent`. |
| `PublicOtel.Mobile/AppShell.xaml.cs` | Constructor takes `IServiceProvider`. **Unconditional.** |

### Modified outside the solution

`PublicOtel.Templates/templates/publicotel-aspire-maui/**` (the ported copy), `.template.config/{template,dotnetcli.host,ide.host}.json`, `.github/workflows/pack.yml`, `PublicOtel.Templates/PublicOtel.Templates.csproj`, `PublicOtel.Templates/README.md`, `README.md`.

---

### Task 1: Branch, and validate the xunit.v3 bump

The spec flags this as the one item to prove before anything is built on it: `Akka.TestKit.Xunit` 1.5.71 needs `xunit.v3.*` 3.2.2, the repo pins `xunit.v3` 3.0.1, and `Reqnroll.xUnit.v3` 3.3.4 also depends on those assemblies. If they cannot coexist, everything in Task 3 onwards changes.

**Files:**
- Modify: `PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: a `PublicOtel.ClientTests` project on `xunit.v3` 3.2.2 with `Akka.TestKit.Xunit` 1.5.71 available, referencing `PublicOtel.ApiService`.

- [ ] **Step 1: Create the branch**

```powershell
git checkout -b feature/include-akka
```

- [ ] **Step 2: Confirm the existing tests are green before touching anything**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`
Expected: PASS. Record the test count — the same tests must still pass at the end of this task.

- [ ] **Step 3: Bump xunit.v3 and add the Akka test packages**

In `PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`, change the `xunit.v3` version and add two items. The file uses tabs.

Replace:

```xml
    <PackageReference Include="xunit.v3" Version="3.0.1" />
```

with:

```xml
    <!-- 3.2.2, not 3.0.1: Akka.TestKit.Xunit 1.5.71 depends on the 3.2.2 xunit.v3
         extensibility and assert assemblies. Reqnroll.xUnit.v3 3.3.4 asks only for >= 2.0.0
         of the same assemblies, so it is satisfied by this. -->
    <PackageReference Include="xunit.v3" Version="3.2.2" />
```

Then add to the second `ItemGroup` (the one with NSubstitute and Shouldly):

```xml
    <!-- Akka.TestKit.Xunit is the xUnit *v3* TestKit, despite reading like the v1-era
         package. Akka.TestKit.Xunit2 is the xUnit v2 one; there is no Akka.TestKit.Xunit3. -->
    <PackageReference Include="Akka.TestKit.Xunit" Version="1.5.71" />
```

And add to the `ItemGroup` holding the existing `ProjectReference`:

```xml
    <ProjectReference Include="..\PublicOtel.ApiService\PublicOtel.ApiService.csproj" />
```

- [ ] **Step 4: Restore and re-run the existing tests**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`
Expected: PASS, with the same test count as Step 2. No `NU1605` downgrade errors and no `NU1107` version conflicts.

If this fails with a package conflict, **stop and report** — the spec's fallback is to drop TestKit-based actor tests and cover `TraceEnvelope` alone. Do not improvise a different test framework.

- [ ] **Step 5: Commit**

```powershell
git add PublicOtel.ClientTests/PublicOtel.ClientTests.csproj
git commit -m "chore: move ClientTests to xunit.v3 3.2.2 and add Akka TestKit

Akka.TestKit.Xunit 1.5.71 (the xUnit v3 TestKit) needs the 3.2.2 extensibility
assemblies. Reqnroll.xUnit.v3 3.3.4 requires only >= 2.0.0 and is satisfied.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Trace envelope and the API's ActivitySource

The smallest piece with real logic, and the one the whole tracing story rests on.

**Files:**
- Create: `PublicOtel.ApiService/Telemetry/ApiTelemetry.cs`
- Create: `PublicOtel.ApiService/Actors/TraceEnvelope.cs`
- Test: `PublicOtel.ClientTests/Actors/TraceEnvelopeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `PublicOtel.ApiService.Telemetry.ApiTelemetry.ActivitySourceName` (`const string`) and `ApiTelemetry.Source` (`static ActivitySource`).
  - `PublicOtel.ApiService.Actors.TraceEnvelope.Current()` → `ActivityContext`.

- [ ] **Step 1: Write the failing test**

Create `PublicOtel.ClientTests/Actors/TraceEnvelopeTests.cs` (tabs):

```csharp
using System.Diagnostics;
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ClientTests.Actors;

/// <summary>
/// TraceEnvelope is what an endpoint uses to put the caller's trace context inside an actor
/// message. Nothing propagates context across a mailbox on its own, so if this returns the
/// wrong thing the waterfall breaks and every actor span becomes a root.
/// </summary>
public class TraceEnvelopeTests
{
	/// <summary>
	/// Activity.Current is null unless something is listening, so a test that wants a real
	/// activity has to register a listener that samples.
	/// </summary>
	private static ActivityListener ListenTo(string sourceName)
	{
		var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
		};

		ActivitySource.AddActivityListener(listener);
		return listener;
	}

	[Fact]
	public void Current_returns_the_ambient_activity_context()
	{
		using var listener = ListenTo(ApiTelemetry.ActivitySourceName);
		using var activity = ApiTelemetry.Source.StartActivity("test");

		activity.ShouldNotBeNull();

		var captured = TraceEnvelope.Current();

		captured.TraceId.ShouldBe(activity.TraceId);
		captured.SpanId.ShouldBe(activity.SpanId);
	}

	[Fact]
	public void Current_returns_default_when_nothing_is_listening()
	{
		// No listener registered, so StartActivity returns null and Activity.Current stays
		// null. This must not throw: sampling decisions are not the caller's problem.
		Activity.Current.ShouldBeNull();

		TraceEnvelope.Current().ShouldBe(default(ActivityContext));
	}

	[Fact]
	public void A_child_activity_started_from_a_captured_context_has_that_context_as_parent()
	{
		using var listener = ListenTo(ApiTelemetry.ActivitySourceName);

		ActivityContext captured;
		using (var caller = ApiTelemetry.Source.StartActivity("caller"))
		{
			captured = TraceEnvelope.Current();
		}

		// This is exactly what the actor does on the far side of the mailbox.
		using var child = ApiTelemetry.Source.StartActivity(
			"actor", ActivityKind.Consumer, parentContext: captured);

		child.ShouldNotBeNull();
		child.ParentSpanId.ShouldBe(captured.SpanId);
		child.TraceId.ShouldBe(captured.TraceId);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~TraceEnvelopeTests"`
Expected: FAIL to compile — `ApiTelemetry` and `TraceEnvelope` do not exist.

- [ ] **Step 3: Write the implementation**

Create `PublicOtel.ApiService/Telemetry/ApiTelemetry.cs` (four spaces):

```csharp
using System.Diagnostics;

namespace PublicOtel.ApiService.Telemetry;

/// <summary>
/// The API service's own <see cref="ActivitySource"/>.
/// </summary>
/// <remarks>
/// The name is deliberately the assembly name, which is also
/// <c>builder.Environment.ApplicationName</c>. PublicOtel.ServiceDefaults already calls
/// <c>tracing.AddSource(builder.Environment.ApplicationName)</c>, so spans started here are
/// exported with no further registration. The coupling is invisible from either file, which
/// is why it is written down here.
/// </remarks>
public static class ApiTelemetry
{
    public const string ActivitySourceName = "PublicOtel.ApiService";

    public static ActivitySource Source { get; } = new(ActivitySourceName);
}
```

Create `PublicOtel.ApiService/Actors/TraceEnvelope.cs` (four spaces):

```csharp
using System.Diagnostics;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Captures the ambient trace context so it can be carried inside an actor message.
/// </summary>
public static class TraceEnvelope
{
    /// <summary>
    /// The current activity's context, or <c>default</c> when there is no current activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never write this as <c>Activity.Current!.Context</c>. Activity.Current is null whenever
    /// no listener is registered or the trace was not sampled, and a NullReferenceException on
    /// a sampling decision is a genuinely miserable bug to find.
    /// </para>
    /// <para>
    /// A default ActivityContext is harmless downstream: StartActivity treats it as "no
    /// parent" and falls back to the ambient context, which is exactly the behaviour you want.
    /// </para>
    /// </remarks>
    public static ActivityContext Current() => Activity.Current?.Context ?? default;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~TraceEnvelopeTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```powershell
git add PublicOtel.ApiService/Telemetry PublicOtel.ApiService/Actors PublicOtel.ClientTests/Actors
git commit -m "feat: add API ActivitySource and trace envelope helper

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Messages, the hub contract, and WeatherStationActor

**Files:**
- Create: `PublicOtel.ApiService/Actors/WeatherMessages.cs`
- Create: `PublicOtel.ApiService/Actors/InvalidReadingException.cs`
- Create: `PublicOtel.ApiService/Hubs/WeatherHub.cs`
- Create: `PublicOtel.ApiService/Actors/WeatherStationActor.cs`
- Modify: `PublicOtel.ApiService/PublicOtel.ApiService.csproj`
- Test: `PublicOtel.ClientTests/Actors/WeatherStationActorTests.cs`

**Interfaces:**
- Consumes: `ApiTelemetry.Source`, `TraceEnvelope` (Task 2).
- Produces:
  - `ReportReading(string Station, int TemperatureC, ActivityContext TraceContext)`
  - `GetLatestReading(string Station, ActivityContext TraceContext)`
  - `StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt)`
  - `NoReadingYet(string Station)`
  - `InvalidReadingException(string station, int temperatureC)` with `.Station`, `.TemperatureC`
  - `IWeatherClient.ReadingReported(StationReading reading)` → `Task`
  - `WeatherHub : Hub<IWeatherClient>`
  - `WeatherStationActor.CreateProps(string station, IHubContext<WeatherHub, IWeatherClient> hub)` → `Props`

- [ ] **Step 1: Add the Akka.Hosting package reference**

In `PublicOtel.ApiService/PublicOtel.ApiService.csproj`, add to the `ItemGroup` containing `Microsoft.AspNetCore.OpenApi`:

```xml
    <PackageReference Include="Akka.Hosting" Version="1.5.71" />
```

SignalR needs no package; it ships in the web SDK.

- [ ] **Step 2: Write the failing test**

Create `PublicOtel.ClientTests/Actors/WeatherStationActorTests.cs` (tabs):

```csharp
using System.Diagnostics;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Hubs;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ClientTests.Actors;

/// <summary>
/// The actor is the point of the whole feature, so it gets real tests: state survives across
/// messages, Ask answers before any state exists, and the span it starts is parented to the
/// context that arrived in the message rather than floating free.
/// </summary>
public class WeatherStationActorTests : TestKit
{
	private readonly IHubContext<WeatherHub, IWeatherClient> _hub =
		Substitute.For<IHubContext<WeatherHub, IWeatherClient>>();

	private readonly IWeatherClient _allClients = Substitute.For<IWeatherClient>();

	public WeatherStationActorTests()
	{
		var clients = Substitute.For<IHubClients<IWeatherClient>>();
		clients.All.Returns(_allClients);
		_hub.Clients.Returns(clients);
	}

	private IActorRef StationActor(string station = "north") =>
		Sys.ActorOf(WeatherStationActor.CreateProps(station, _hub));

	[Fact]
	public void Ask_before_any_report_answers_NoReadingYet()
	{
		var actor = StationActor();

		actor.Tell(new GetLatestReading("north", default));

		ExpectMsg<NoReadingYet>().Station.ShouldBe("north");
	}

	[Fact]
	public void A_reported_reading_is_remembered_and_returned_on_the_next_Ask()
	{
		var actor = StationActor();

		actor.Tell(new ReportReading("north", 21, default));
		actor.Tell(new GetLatestReading("north", default));

		var reading = ExpectMsg<StationReading>();
		reading.Station.ShouldBe("north");
		reading.TemperatureC.ShouldBe(21);
	}

	[Fact]
	public void The_latest_reported_reading_replaces_the_previous_one()
	{
		var actor = StationActor();

		actor.Tell(new ReportReading("north", 21, default));
		actor.Tell(new ReportReading("north", 25, default));
		actor.Tell(new GetLatestReading("north", default));

		ExpectMsg<StationReading>().TemperatureC.ShouldBe(25);
	}

	[Fact]
	public async Task A_reported_reading_is_broadcast_to_every_connected_client()
	{
		var actor = StationActor();

		actor.Tell(new ReportReading("north", 21, default));

		// The Tell is asynchronous, so wait for the actor to become idle by Asking it
		// something and waiting for the reply.
		actor.Tell(new GetLatestReading("north", default));
		ExpectMsg<StationReading>();

		await _allClients.Received(1).ReadingReported(
			Arg.Is<StationReading>(r => r.Station == "north" && r.TemperatureC == 21));
	}

	[Fact]
	public void An_out_of_range_reading_throws_so_the_supervisor_can_decide()
	{
		var actor = StationActor();

		// EventFilter swallows the expected error log; without it the exception is noise.
		EventFilter.Exception<InvalidReadingException>().ExpectOne(() =>
		{
			actor.Tell(new ReportReading("north", 5000, default));
		});
	}

	[Fact]
	public void The_span_the_actor_starts_is_parented_to_the_context_in_the_message()
	{
		var started = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == ApiTelemetry.ActivitySourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStarted = started.Add,
		};
		ActivitySource.AddActivityListener(listener);

		ActivityContext callerContext;
		using (var caller = ApiTelemetry.Source.StartActivity("caller"))
		{
			callerContext = TraceEnvelope.Current();
		}

		var actor = StationActor();
		actor.Tell(new ReportReading("north", 21, callerContext));
		actor.Tell(new GetLatestReading("north", callerContext));
		ExpectMsg<StationReading>();

		var actorSpan = started.ShouldContain(a => a.OperationName == "WeatherStationActor.ReportReading");
		actorSpan.ParentSpanId.ShouldBe(callerContext.SpanId);
		actorSpan.TraceId.ShouldBe(callerContext.TraceId);
	}
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~WeatherStationActorTests"`
Expected: FAIL to compile — the message types, hub and actor do not exist.

- [ ] **Step 4: Write the messages and the exception**

Create `PublicOtel.ApiService/Actors/WeatherMessages.cs` (four spaces):

```csharp
using System.Diagnostics;

namespace PublicOtel.ApiService.Actors;

// These are records, and that is not decoration. After Tell returns, the sender still holds a
// reference to the message it just sent. If the message were mutable, two actors would be
// sharing mutable state across a thread boundary - the exact thing the actor model exists to
// remove. Immutable message, no shared state, no lock.
//
// Every inbound message carries an ActivityContext. Auto-instrumentation propagates trace
// context across an HTTP hop for free, via the W3C traceparent header. Nothing propagates it
// across a mailbox: Tell returns immediately and the actor handles the message later, on
// another thread, with no ambient Activity. Carrying the context explicitly is the whole
// trick, and it is the difference between one waterfall and a pile of orphan spans.

/// <summary>Report a new reading for a station. Sent with <c>Tell</c>; there is no reply.</summary>
public sealed record ReportReading(string Station, int TemperatureC, ActivityContext TraceContext);

/// <summary>Ask a station for its most recent reading.</summary>
public sealed record GetLatestReading(string Station, ActivityContext TraceContext);

/// <summary>
/// A station's current reading. Both the reply to <see cref="GetLatestReading"/> and the
/// payload broadcast to SignalR clients.
/// </summary>
public sealed record StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt);

/// <summary>Reply when a station has not reported anything yet.</summary>
public sealed record NoReadingYet(string Station);
```

Create `PublicOtel.ApiService/Actors/InvalidReadingException.cs` (four spaces):

```csharp
namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Thrown for a physically implausible reading. It exists so that
/// <see cref="StationSupervisor"/> has a real failure to make a decision about, rather than a
/// contrived one.
/// </summary>
public sealed class InvalidReadingException(string station, int temperatureC)
    : Exception($"Station '{station}' reported {temperatureC}°C, which is outside the supported range.")
{
    public string Station { get; } = station;

    public int TemperatureC { get; } = temperatureC;
}
```

- [ ] **Step 5: Write the hub contract**

Create `PublicOtel.ApiService/Hubs/WeatherHub.cs` (four spaces):

```csharp
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Actors;

namespace PublicOtel.ApiService.Hubs;

/// <summary>
/// The methods the server can call on a connected client. Declaring them on an interface and
/// using <see cref="Hub{T}"/> means a typo in a client method name is a compile error instead
/// of a message that silently goes nowhere.
/// </summary>
public interface IWeatherClient
{
    Task ReadingReported(StationReading reading);
}

/// <summary>
/// The hub at <c>/hubs/weather</c>. It deliberately has no methods of its own: clients only
/// listen here, and report readings over HTTP instead.
/// </summary>
/// <remarks>
/// That split is on purpose. An HTTP POST carries a <c>traceparent</c> header and is picked up
/// by ASP.NET Core auto-instrumentation, so the report is already inside a trace by the time
/// it reaches the endpoint. A hub method invocation is not, and would start the story with a
/// broken trace.
/// </remarks>
public sealed class WeatherHub : Hub<IWeatherClient>;
```

- [ ] **Step 6: Write the actor**

Create `PublicOtel.ApiService/Actors/WeatherStationActor.cs` (four spaces):

```csharp
using System.Diagnostics;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// One actor per weather station, owning that station's latest reading.
/// </summary>
/// <remarks>
/// <para>
/// The contract: <c>_latest</c> is private, the mailbox delivers one message at a time, and
/// nothing else in the process can touch that field. That is why there is no lock here and
/// why there does not need to be one.
/// </para>
/// <para>
/// One actor per station rather than one actor holding a dictionary of stations: two stations
/// reporting at the same moment are then genuinely concurrent, because they are different
/// mailboxes. A single actor with a dictionary would serialise the whole system through one
/// queue.
/// </para>
/// </remarks>
public sealed class WeatherStationActor : ReceiveActor
{
    private const int MinTemperatureC = -90;
    private const int MaxTemperatureC = 60;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;

    private StationReading? _latest;

    public WeatherStationActor(string station, IHubContext<WeatherHub, IWeatherClient> hub)
    {
        _hub = hub;

        _log.Debug("Station actor for {0} started.", station);

        ReceiveAsync<ReportReading>(HandleReportAsync);
        Receive<GetLatestReading>(HandleGet);
    }

    public static Props CreateProps(string station, IHubContext<WeatherHub, IWeatherClient> hub) =>
        Props.Create(() => new WeatherStationActor(station, hub));

    private async Task HandleReportAsync(ReportReading message)
    {
        // parentContext is the entire fix. Delete that one argument and this span becomes a
        // root: same trace id, no parent, floating next to the API span in the dashboard
        // instead of underneath it. That is the broken waterfall from the Monday lecture, and
        // reproducing it really is this cheap.
        using var activity = ApiTelemetry.Source.StartActivity(
            "WeatherStationActor.ReportReading",
            ActivityKind.Consumer,
            parentContext: message.TraceContext);

        activity?.SetTag("station.id", message.Station);
        activity?.SetTag("station.temperature_c", message.TemperatureC);

        if (message.TemperatureC is < MinTemperatureC or > MaxTemperatureC)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Reading out of range.");

            // Throwing inside an actor is not the same as throwing inside a request handler.
            // Nobody catches this; the mailbox stops, the parent decides what happens next.
            throw new InvalidReadingException(message.Station, message.TemperatureC);
        }

        _latest = new StationReading(message.Station, message.TemperatureC, DateTimeOffset.UtcNow);
        _log.Info("Station {0} is now {1}°C.", message.Station, message.TemperatureC);

        await _hub.Clients.All.ReadingReported(_latest);
    }

    private void HandleGet(GetLatestReading message)
    {
        using var activity = ApiTelemetry.Source.StartActivity(
            "WeatherStationActor.GetLatestReading",
            ActivityKind.Consumer,
            parentContext: message.TraceContext);

        activity?.SetTag("station.id", message.Station);
        activity?.SetTag("station.has_reading", _latest is not null);

        // Sender is whoever sent this message. For an Ask that is a temporary actor the Ask
        // created, and replying to it is what completes the caller's Task.
        Sender.Tell(_latest is null ? new NoReadingYet(message.Station) : _latest);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~WeatherStationActorTests"`
Expected: PASS, 6 tests.

- [ ] **Step 8: Commit**

```powershell
git add PublicOtel.ApiService PublicOtel.ClientTests/Actors
git commit -m "feat: add WeatherStationActor with trace-carrying messages

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: StationSupervisor

**Files:**
- Create: `PublicOtel.ApiService/Actors/StationSupervisor.cs`
- Test: `PublicOtel.ClientTests/Actors/StationSupervisorTests.cs`

**Interfaces:**
- Consumes: `WeatherStationActor.CreateProps`, the message records, `InvalidReadingException` (Task 3).
- Produces: `StationSupervisor.CreateProps(IHubContext<WeatherHub, IWeatherClient> hub)` → `Props`.

- [ ] **Step 1: Write the failing test**

Create `PublicOtel.ClientTests/Actors/StationSupervisorTests.cs` (tabs):

```csharp
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ClientTests.Actors;

/// <summary>
/// The supervisor is where "actor per entity" and "supervision is the parent's job" actually
/// live, so both are tested here rather than described in a comment.
/// </summary>
public class StationSupervisorTests : TestKit
{
	private readonly IHubContext<WeatherHub, IWeatherClient> _hub =
		Substitute.For<IHubContext<WeatherHub, IWeatherClient>>();

	public StationSupervisorTests()
	{
		var clients = Substitute.For<IHubClients<IWeatherClient>>();
		clients.All.Returns(Substitute.For<IWeatherClient>());
		_hub.Clients.Returns(clients);
	}

	private IActorRef Supervisor() => Sys.ActorOf(StationSupervisor.CreateProps(_hub));

	[Fact]
	public void Each_station_gets_its_own_actor_with_its_own_state()
	{
		var supervisor = Supervisor();

		supervisor.Tell(new ReportReading("north", 21, default));
		supervisor.Tell(new ReportReading("south", 30, default));

		supervisor.Tell(new GetLatestReading("north", default));
		ExpectMsg<StationReading>().TemperatureC.ShouldBe(21);

		supervisor.Tell(new GetLatestReading("south", default));
		ExpectMsg<StationReading>().TemperatureC.ShouldBe(30);
	}

	[Fact]
	public void An_unknown_station_answers_rather_than_failing()
	{
		var supervisor = Supervisor();

		supervisor.Tell(new GetLatestReading("nowhere", default));

		ExpectMsg<NoReadingYet>().Station.ShouldBe("nowhere");
	}

	[Fact]
	public void A_bad_reading_restarts_the_child_and_its_state_is_gone()
	{
		var supervisor = Supervisor();

		supervisor.Tell(new ReportReading("north", 21, default));
		supervisor.Tell(new GetLatestReading("north", default));
		ExpectMsg<StationReading>().TemperatureC.ShouldBe(21);

		EventFilter.Exception<InvalidReadingException>().ExpectOne(() =>
		{
			supervisor.Tell(new ReportReading("north", 5000, default));
		});

		// Restart builds a fresh instance of the actor class, so the field is back to null.
		// This is the cost of the restart directive, and it is the half of the supervision
		// story people forget.
		supervisor.Tell(new GetLatestReading("north", default));
		ExpectMsg<NoReadingYet>();
	}

	[Fact]
	public void A_restarted_station_keeps_working()
	{
		var supervisor = Supervisor();

		EventFilter.Exception<InvalidReadingException>().ExpectOne(() =>
		{
			supervisor.Tell(new ReportReading("north", 5000, default));
		});

		supervisor.Tell(new ReportReading("north", 18, default));
		supervisor.Tell(new GetLatestReading("north", default));

		ExpectMsg<StationReading>().TemperatureC.ShouldBe(18);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~StationSupervisorTests"`
Expected: FAIL to compile — `StationSupervisor` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PublicOtel.ApiService/Actors/StationSupervisor.cs` (four spaces):

```csharp
using Akka.Actor;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Parent of one <see cref="WeatherStationActor"/> per station id - the actor-per-entity
/// pattern - and the place where failure decisions are made.
/// </summary>
public sealed class StationSupervisor : ReceiveActor
{
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;

    public StationSupervisor(IHubContext<WeatherHub, IWeatherClient> hub)
    {
        _hub = hub;

        // Forward, not Tell: Forward preserves the original Sender, so an Ask that arrives
        // here gets its reply straight from the child. Tell would make the supervisor the
        // sender and the caller's Ask would time out.
        Receive<ReportReading>(message => ChildFor(message.Station).Forward(message));
        Receive<GetLatestReading>(message => ChildFor(message.Station).Forward(message));
    }

    public static Props CreateProps(IHubContext<WeatherHub, IWeatherClient> hub) =>
        Props.Create(() => new StationSupervisor(hub));

    /// <summary>
    /// What this actor does when one of its children throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is supervision: the failure is not handled where it happened, it is handled by the
    /// parent, which is the only party that knows what the child is for.
    /// </para>
    /// <para>
    /// <c>Restart</c> replaces the failed child with a fresh instance, so its <c>_latest</c>
    /// is gone. That is a real cost and it is chosen deliberately: a station that produced an
    /// impossible reading has no state worth keeping. <c>Escalate</c> hands anything
    /// unrecognised to this actor's own parent rather than guessing at a policy for it.
    /// </para>
    /// <para>
    /// OneForOne, not AllForOne: one station misbehaving says nothing about the others.
    /// </para>
    /// </remarks>
    protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(
        maxNrOfRetries: 3,
        withinTimeRange: TimeSpan.FromMinutes(1),
        localOnlyDecider: exception => exception switch
        {
            InvalidReadingException => Directive.Restart,
            _ => Directive.Escalate,
        });

    private IActorRef ChildFor(string station)
    {
        // Actor names have to be URL-safe and stable, and a station id arrives from a route
        // parameter, so it is escaped rather than trusted.
        var name = $"station-{Uri.EscapeDataString(station)}";
        var child = Context.Child(name);

        return child.Equals(ActorRefs.Nobody)
            ? Context.ActorOf(WeatherStationActor.CreateProps(station, _hub), name)
            : child;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~StationSupervisorTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the whole test project**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`
Expected: PASS. Nothing from Tasks 1-3 regressed.

- [ ] **Step 6: Commit**

```powershell
git add PublicOtel.ApiService/Actors/StationSupervisor.cs PublicOtel.ClientTests/Actors/StationSupervisorTests.cs
git commit -m "feat: add StationSupervisor with actor-per-station routing and restart policy

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Akka.Hosting registration, endpoints, and Program.cs

**Files:**
- Create: `PublicOtel.ApiService/Actors/WeatherActorExtensions.cs`
- Create: `PublicOtel.ApiService/Actors/WeatherStationEndpoints.cs`
- Modify: `PublicOtel.ApiService/Program.cs`

**Interfaces:**
- Consumes: `StationSupervisor.CreateProps`, the message records, `TraceEnvelope.Current()`, `WeatherHub`.
- Produces:
  - `AddWeatherActors(this IHostApplicationBuilder)` → `IHostApplicationBuilder`
  - `MapWeatherStationEndpoints(this IEndpointRouteBuilder)` → `IEndpointRouteBuilder`
  - `ReadingRequest(int TemperatureC)` — the POST body
  - HTTP surface: `POST /stations/{station}/readings` → 202, `GET /stations/{station}` → 200 `StationReading` or 404

This task has no unit test. The endpoints are four lines of wiring each and the logic under them is already covered by Tasks 2-4; the real verification is Task 15's manual acceptance run. Its gate is a clean build.

- [ ] **Step 1: Write the Akka.Hosting registration**

Create `PublicOtel.ApiService/Actors/WeatherActorExtensions.cs` (four spaces):

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Puts the actor system in the DI container and starts the one top-level actor.
/// </summary>
public static class WeatherActorExtensions
{
    // Lower-case on purpose: in the template this literal is rewritten per generated app by
    // the lowerCaseName symbol, so each app names its actor system after itself.
    private const string ActorSystemName = "publicotel";

    public static IHostApplicationBuilder AddWeatherActors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAkka(ActorSystemName, (akka, serviceProvider) =>
        {
            akka.WithActors((system, registry) =>
            {
                // The hub context is resolved once, here, and handed to the actors. Actors
                // must not reach into the container themselves: an actor's dependencies
                // belong in its Props so that a test can supply different ones.
                var hub = serviceProvider.GetRequiredService<IHubContext<WeatherHub, IWeatherClient>>();

                var supervisor = system.ActorOf(StationSupervisor.CreateProps(hub), "stations");

                // Registering by type is what lets an endpoint take IRequiredActor<StationSupervisor>.
                registry.Register<StationSupervisor>(supervisor);
            });
        });

        return builder;
    }
}
```

- [ ] **Step 2: Write the endpoints**

Create `PublicOtel.ApiService/Actors/WeatherStationEndpoints.cs` (four spaces):

```csharp
using Akka.Actor;
using Akka.Hosting;

namespace PublicOtel.ApiService.Actors;

/// <summary>The body of a reading report.</summary>
public sealed record ReadingRequest(int TemperatureC);

public static class WeatherStationEndpoints
{
    /// <summary>
    /// How long the Ask endpoint waits for an actor to reply. There is no such thing as an
    /// Ask without a timeout; declining to pick one just means accepting a default.
    /// </summary>
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapWeatherStationEndpoints(this IEndpointRouteBuilder app)
    {
        // Tell: fire and forget. 202 Accepted is the honest status code - the actor has not
        // necessarily processed this yet, and saying 200 OK would be a small lie about what
        // asynchronous messaging is.
        app.MapPost("/stations/{station}/readings",
            (string station, ReadingRequest request, IRequiredActor<StationSupervisor> stations) =>
            {
                stations.ActorRef.Tell(
                    new ReportReading(station, request.TemperatureC, TraceEnvelope.Current()));

                return Results.Accepted($"/stations/{station}");
            })
            .WithName("ReportReading");

        // Ask: request/response faked on top of asynchronous messaging. What it costs, in
        // full: a timeout you have to choose, a temporary actor and a continuation held open
        // while you wait, and an AskTimeoutException failure mode that Tell simply does not
        // have. Worth it only when the HTTP response body needs the answer - as it does here.
        app.MapGet("/stations/{station}",
            async (string station, IRequiredActor<StationSupervisor> stations) =>
            {
                var reply = await stations.ActorRef.Ask(
                    new GetLatestReading(station, TraceEnvelope.Current()),
                    AskTimeout);

                return reply switch
                {
                    StationReading reading => Results.Ok(reading),
                    _ => Results.NotFound(),
                };
            })
            .WithName("GetLatestReading");

        return app;
    }
}
```

- [ ] **Step 3: Wire it into Program.cs**

In `PublicOtel.ApiService/Program.cs`, add these usings at the very top of the file:

```csharp
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Hubs;
```

After the line `builder.Services.AddOpenApi();` add:

```csharp
// SignalR is what lets the server tell the phone something happened, instead of the phone
// having to keep asking.
builder.Services.AddSignalR();

// The actor system goes in the container alongside everything else.
builder.AddWeatherActors();
```

After the line `app.MapGet("/", () => "API service is running...");` — that is, alongside the other endpoint registrations and before `app.MapDefaultEndpoints();` — add:

```csharp
app.MapHub<WeatherHub>("/hubs/weather");
app.MapWeatherStationEndpoints();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build PublicOtel.ApiService/PublicOtel.ApiService.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Re-run the test project**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`
Expected: PASS. The `ProjectReference` means an ApiService compile error would surface here too.

- [ ] **Step 6: Commit**

```powershell
git add PublicOtel.ApiService
git commit -m "feat: register the actor system and expose Tell/Ask station endpoints

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Client API address resolution and the station HTTP client

**Files:**
- Create: `PublicOtel.ClientLogic/Realtime/ApiServiceAddress.cs`
- Create: `PublicOtel.ClientLogic/Realtime/StationApiClient.cs`
- Modify: `PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj`
- Test: `PublicOtel.ClientTests/Realtime/ApiServiceAddressTests.cs`
- Test: `PublicOtel.ClientTests/Realtime/StationApiClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `PublicOtel.ClientLogic.Realtime.StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt)` — the **client-side** copy of the DTO
  - `ApiServiceAddress.Resolve(IConfiguration configuration, string serviceName = "apiservice")` → `Uri`
  - `IStationApiClient.ReportReadingAsync(string station, int temperatureC, CancellationToken)` → `Task`
  - `IStationApiClient.GetLatestReadingAsync(string station, CancellationToken)` → `Task<StationReading?>`
  - `StationApiClient(HttpClient httpClient)`

- [ ] **Step 1: Add the package references**

In `PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj`, add to the existing `ItemGroup` (tabs):

```xml
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.11" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.11" />
```

- [ ] **Step 2: Write the failing tests**

Create `PublicOtel.ClientTests/Realtime/ApiServiceAddressTests.cs` (tabs):

```csharp
using Microsoft.Extensions.Configuration;
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.ClientTests.Realtime;

/// <summary>
/// HttpClient gets Aspire service discovery for free through AddServiceDiscovery(). SignalR's
/// HubConnection does not go through HttpClientFactory, so it needs the address looked up
/// explicitly - which is what this does, from the same keys Aspire injects.
/// </summary>
public class ApiServiceAddressTests
{
	private static IConfiguration Config(params (string Key, string Value)[] entries) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
			.Build();

	[Fact]
	public void Resolve_prefers_the_https_endpoint()
	{
		var configuration = Config(
			("services:apiservice:https:0", "https://localhost:7301"),
			("services:apiservice:http:0", "http://localhost:5301"));

		ApiServiceAddress.Resolve(configuration).ShouldBe(new Uri("https://localhost:7301"));
	}

	[Fact]
	public void Resolve_falls_back_to_http_when_there_is_no_https_endpoint()
	{
		var configuration = Config(("services:apiservice:http:0", "http://localhost:5301"));

		ApiServiceAddress.Resolve(configuration).ShouldBe(new Uri("http://localhost:5301"));
	}

	[Fact]
	public void Resolve_throws_a_useful_message_when_the_app_was_not_launched_by_the_AppHost()
	{
		var exception = Should.Throw<InvalidOperationException>(() => ApiServiceAddress.Resolve(Config()));

		exception.Message.ShouldContain("apiservice");
		exception.Message.ShouldContain("AppHost");
	}
}
```

Create `PublicOtel.ClientTests/Realtime/StationApiClientTests.cs` (tabs):

```csharp
using System.Net;
using System.Text;
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.ClientTests.Realtime;

public class StationApiClientTests
{
	/// <summary>
	/// A stub handler is used rather than a substituted HttpClient because HttpClient is a
	/// concrete class - SendAsync is the only seam it has.
	/// </summary>
	private sealed class StubHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
	{
		public HttpRequestMessage? LastRequest { get; private set; }

		public string? LastBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = request;
			LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
		}
	}

	private static StationApiClient ClientFor(StubHandler handler) =>
		new(new HttpClient(handler) { BaseAddress = new Uri("https://apiservice") });

	[Fact]
	public async Task ReportReading_posts_the_temperature_to_the_station_route()
	{
		var handler = new StubHandler(HttpStatusCode.Accepted);

		await ClientFor(handler).ReportReadingAsync("north", 21);

		handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
		handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/stations/north/readings");
		handler.LastBody.ShouldContain("21");
	}

	[Fact]
	public async Task ReportReading_escapes_a_station_name_that_needs_it()
	{
		var handler = new StubHandler(HttpStatusCode.Accepted);

		await ClientFor(handler).ReportReadingAsync("north side", 21);

		handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/stations/north%20side/readings");
	}

	[Fact]
	public async Task GetLatestReading_returns_the_reading()
	{
		var handler = new StubHandler(
			HttpStatusCode.OK,
			"""{"station":"north","temperatureC":21,"observedAt":"2026-09-21T10:00:00+00:00"}""");

		var reading = await ClientFor(handler).GetLatestReadingAsync("north");

		reading.ShouldNotBeNull();
		reading.Station.ShouldBe("north");
		reading.TemperatureC.ShouldBe(21);
	}

	[Fact]
	public async Task GetLatestReading_returns_null_for_a_station_that_has_not_reported()
	{
		var handler = new StubHandler(HttpStatusCode.NotFound);

		(await ClientFor(handler).GetLatestReadingAsync("nowhere")).ShouldBeNull();
	}

	[Fact]
	public async Task GetLatestReading_throws_on_a_server_error()
	{
		var handler = new StubHandler(HttpStatusCode.InternalServerError);

		await Should.ThrowAsync<HttpRequestException>(
			() => ClientFor(handler).GetLatestReadingAsync("north"));
	}
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~Realtime"`
Expected: FAIL to compile — the `Realtime` namespace does not exist.

- [ ] **Step 4: Write the implementation**

Create `PublicOtel.ClientLogic/Realtime/ApiServiceAddress.cs` (tabs):

```csharp
using Microsoft.Extensions.Configuration;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// Finds the API service's base address in the service discovery keys Aspire injects as
/// environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Typed HttpClients never need this: AddServiceDiscovery() rewrites "https+http://apiservice"
/// at send time. SignalR's HubConnection is built directly and never touches
/// HttpClientFactory, so the address has to be resolved by hand exactly once.
/// </para>
/// <para>
/// The keys are <c>services__apiservice__https__0</c> and <c>services__apiservice__http__0</c>.
/// PublicOtel.MauiServiceDefaults calls <c>Configuration.AddEnvironmentVariables()</c>
/// specifically so they are visible here - MauiApp.CreateBuilder() starts with empty
/// configuration, unlike the ASP.NET Core host.
/// </para>
/// </remarks>
public static class ApiServiceAddress
{
	public static Uri Resolve(IConfiguration configuration, string serviceName = "apiservice")
	{
		var address = configuration[$"services:{serviceName}:https:0"]
			?? configuration[$"services:{serviceName}:http:0"]
			?? throw new InvalidOperationException(
				$"No service discovery entry for '{serviceName}'. This app has to be started by "
				+ "the Aspire AppHost, which is what injects the address.");

		return new Uri(address, UriKind.Absolute);
	}
}
```

Create `PublicOtel.ClientLogic/Realtime/StationApiClient.cs` (tabs):

```csharp
using System.Net;
using System.Net.Http.Json;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// A station's current reading, as the client sees it.
/// </summary>
/// <remarks>
/// Deliberately a separate declaration from the API's <c>StationReading</c>. There is no
/// shared contracts project, and inventing one to save four lines would couple the client's
/// release to the server's.
/// </remarks>
public record StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt);

public interface IStationApiClient
{
	Task ReportReadingAsync(string station, int temperatureC, CancellationToken cancellationToken = default);

	Task<StationReading?> GetLatestReadingAsync(string station, CancellationToken cancellationToken = default);
}

public sealed class StationApiClient(HttpClient httpClient) : IStationApiClient
{
	public async Task ReportReadingAsync(
		string station, int temperatureC, CancellationToken cancellationToken = default)
	{
		// An ordinary HTTP POST, which means it carries a traceparent header and the trace
		// starts here rather than at the actor.
		using var response = await httpClient.PostAsJsonAsync(
			$"/stations/{Uri.EscapeDataString(station)}/readings",
			new { TemperatureC = temperatureC },
			cancellationToken);

		response.EnsureSuccessStatusCode();
	}

	public async Task<StationReading?> GetLatestReadingAsync(
		string station, CancellationToken cancellationToken = default)
	{
		using var response = await httpClient.GetAsync(
			$"/stations/{Uri.EscapeDataString(station)}", cancellationToken);

		// A station nobody has reported for is an ordinary answer, not a failure.
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<StationReading>(cancellationToken);
	}
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~Realtime"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```powershell
git add PublicOtel.ClientLogic PublicOtel.ClientTests/Realtime
git commit -m "feat: add station API client and Aspire address resolution

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: The SignalR hub client

**Files:**
- Create: `PublicOtel.ClientLogic/Realtime/StationHubClient.cs`

**Interfaces:**
- Consumes: `StationReading` (Task 6).
- Produces:
  - `IStationHubClient : IAsyncDisposable` with `event Action<StationReading>? ReadingReported`, `event Action? Reconnected`, `Task StartAsync(CancellationToken)`
  - `StationHubClient(Uri hubUri)`

`StationHubClient` is a thin wrapper over `HubConnection`, which cannot be substituted or exercised without a live server. That is precisely why `IStationHubClient` exists: all the behaviour worth testing lives in the view model behind it (Task 8). Keep this class free of logic — if you find yourself adding an `if`, it belongs in the view model.

- [ ] **Step 1: Write the implementation**

Create `PublicOtel.ClientLogic/Realtime/StationHubClient.cs` (tabs):

```csharp
using Microsoft.AspNetCore.SignalR.Client;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// The client half of the SignalR connection. Kept deliberately free of logic so that
/// everything worth testing lives in <see cref="StationsViewModel"/> behind this interface.
/// </summary>
public interface IStationHubClient : IAsyncDisposable
{
	/// <summary>Raised when the server broadcasts a reading. Not raised on the UI thread.</summary>
	event Action<StationReading>? ReadingReported;

	/// <summary>
	/// Raised after the connection comes back. Anything broadcast while it was down was
	/// missed, so the subscriber has to re-fetch state.
	/// </summary>
	event Action? Reconnected;

	Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class StationHubClient : IStationHubClient
{
	/// <summary>
	/// Must match <c>IWeatherClient.ReadingReported</c> on the server. SignalR matches client
	/// methods by name at runtime, so a typo here is a silent no-op rather than an error.
	/// </summary>
	private const string ReadingReportedMethod = "ReadingReported";

	private readonly HubConnection _connection;

	public StationHubClient(Uri hubUri)
	{
		_connection = new HubConnectionBuilder()
			.WithUrl(hubUri)
			.WithAutomaticReconnect()
			.Build();

		_connection.On<StationReading>(
			ReadingReportedMethod,
			reading => ReadingReported?.Invoke(reading));

		_connection.Reconnected += _ =>
		{
			Reconnected?.Invoke();
			return Task.CompletedTask;
		};
	}

	public event Action<StationReading>? ReadingReported;

	public event Action? Reconnected;

	public Task StartAsync(CancellationToken cancellationToken = default) =>
		_connection.StartAsync(cancellationToken);

	public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add PublicOtel.ClientLogic/Realtime/StationHubClient.cs
git commit -m "feat: add SignalR hub client wrapper

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: StationsViewModel

**Files:**
- Create: `PublicOtel.ClientLogic/Realtime/StationsViewModel.cs`
- Test: `PublicOtel.ClientTests/Realtime/StationsViewModelTests.cs`

**Interfaces:**
- Consumes: `IStationApiClient` (Task 6), `IStationHubClient` (Task 7).
- Produces: `StationsViewModel(IStationApiClient stationApi, IStationHubClient hub)` with observable properties `Station`, `TemperatureC`, `LatestReading`, `Status`; `ObservableCollection<StationReading> Updates`; commands `ConnectCommand`, `ReportReadingCommand`.

- [ ] **Step 1: Write the failing test**

Create `PublicOtel.ClientTests/Realtime/StationsViewModelTests.cs` (tabs):

```csharp
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.ClientTests.Realtime;

/// <summary>
/// No emulator, no hub, no network: the view model only ever sees the two interfaces.
/// </summary>
public class StationsViewModelTests
{
	private readonly IStationApiClient _api = Substitute.For<IStationApiClient>();
	private readonly IStationHubClient _hub = Substitute.For<IStationHubClient>();

	private StationsViewModel NewViewModel() => new(_api, _hub);

	private static StationReading Reading(string station = "north", int temperatureC = 21) =>
		new(station, temperatureC, DateTimeOffset.UtcNow);

	[Fact]
	public async Task Connect_starts_the_hub_and_seeds_the_latest_reading()
	{
		_api.GetLatestReadingAsync("north", Arg.Any<CancellationToken>()).Returns(Reading(temperatureC: 14));

		var viewModel = NewViewModel();
		await viewModel.ConnectCommand.ExecuteAsync(null);

		await _hub.Received(1).StartAsync(Arg.Any<CancellationToken>());
		viewModel.LatestReading!.TemperatureC.ShouldBe(14);
		viewModel.Status.ShouldContain("Live");
	}

	[Fact]
	public async Task Connect_copes_with_a_station_that_has_never_reported()
	{
		_api.GetLatestReadingAsync("north", Arg.Any<CancellationToken>()).Returns((StationReading?)null);

		var viewModel = NewViewModel();
		await viewModel.ConnectCommand.ExecuteAsync(null);

		viewModel.LatestReading.ShouldBeNull();
		viewModel.Status.ShouldContain("Live");
	}

	[Fact]
	public async Task A_broadcast_reading_updates_the_view_model()
	{
		var viewModel = NewViewModel();
		await viewModel.ConnectCommand.ExecuteAsync(null);

		// This is the whole point of the feature: nobody asked for this, the server pushed it.
		_hub.ReadingReported += Raise.Event<Action<StationReading>>(Reading(temperatureC: 30));

		viewModel.LatestReading!.TemperatureC.ShouldBe(30);
		viewModel.Updates.Count.ShouldBe(1);
	}

	[Fact]
	public async Task The_newest_broadcast_is_first_in_the_list()
	{
		var viewModel = NewViewModel();
		await viewModel.ConnectCommand.ExecuteAsync(null);

		_hub.ReadingReported += Raise.Event<Action<StationReading>>(Reading(temperatureC: 10));
		_hub.ReadingReported += Raise.Event<Action<StationReading>>(Reading(temperatureC: 20));

		viewModel.Updates[0].TemperatureC.ShouldBe(20);
		viewModel.Updates.Count.ShouldBe(2);
	}

	[Fact]
	public async Task ReportReading_sends_the_selected_station_and_temperature()
	{
		var viewModel = NewViewModel();
		viewModel.Station = "south";
		viewModel.TemperatureC = 33;

		await viewModel.ReportReadingCommand.ExecuteAsync(null);

		await _api.Received(1).ReportReadingAsync("south", 33, Arg.Any<CancellationToken>());
		viewModel.Status.ShouldContain("33");
	}

	[Fact]
	public async Task ReportReading_reports_a_failure_instead_of_throwing()
	{
		_api.ReportReadingAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task>(_ => throw new HttpRequestException("apiservice unreachable"));

		var viewModel = NewViewModel();
		await viewModel.ReportReadingCommand.ExecuteAsync(null);

		viewModel.Status.ShouldContain("apiservice unreachable");
	}

	[Fact]
	public async Task A_reconnect_re_fetches_state_because_pushes_were_missed()
	{
		_api.GetLatestReadingAsync("north", Arg.Any<CancellationToken>()).Returns(Reading(temperatureC: 5));

		var viewModel = NewViewModel();
		await viewModel.ConnectCommand.ExecuteAsync(null);

		_api.GetLatestReadingAsync("north", Arg.Any<CancellationToken>()).Returns(Reading(temperatureC: 40));
		_hub.Reconnected += Raise.Event<Action>();

		// The handler is async void by necessity - an event cannot be awaited - so give the
		// continuation a turn before asserting.
		await Task.Delay(50);

		viewModel.LatestReading!.TemperatureC.ShouldBe(40);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~StationsViewModelTests"`
Expected: FAIL to compile — `StationsViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PublicOtel.ClientLogic/Realtime/StationsViewModel.cs` (tabs):

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// Drives the stations page. Free of MAUI types, like <see cref="WeatherViewModel"/>, so it
/// can be exercised directly from tests.
/// </summary>
public partial class StationsViewModel : ObservableObject
{
	private readonly IStationApiClient _stationApi;
	private readonly IStationHubClient _hub;

	/// <summary>
	/// Captured at construction, when the container resolves this on the UI thread.
	/// </summary>
	/// <remarks>
	/// SignalR raises its callbacks on a background thread, and mutating an
	/// ObservableCollection off the UI thread is what turns a live update into a crash in the
	/// CollectionView. Posting to the captured context keeps this project free of MAUI types.
	/// Under test there is no context, so the update simply runs inline.
	/// </remarks>
	private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

	public StationsViewModel(IStationApiClient stationApi, IStationHubClient hub)
	{
		_stationApi = stationApi;
		_hub = hub;

		_hub.ReadingReported += OnReadingReported;
		_hub.Reconnected += OnReconnected;
	}

	[ObservableProperty]
	public partial string Station { get; set; } = "north";

	[ObservableProperty]
	public partial int TemperatureC { get; set; } = 20;

	[ObservableProperty]
	public partial StationReading? LatestReading { get; set; }

	[ObservableProperty]
	public partial string Status { get; set; } = "Not connected.";

	/// <summary>Newest first, so the most recent push is at the top of the screen.</summary>
	public ObservableCollection<StationReading> Updates { get; } = [];

	[RelayCommand]
	private async Task ConnectAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _hub.StartAsync(cancellationToken);
			LatestReading = await _stationApi.GetLatestReadingAsync(Station, cancellationToken);
			Status = "Live. Report a reading from another device and watch this update.";
		}
		catch (Exception ex)
		{
			Status = $"Could not connect: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task ReportReadingAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _stationApi.ReportReadingAsync(Station, TemperatureC, cancellationToken);
			Status = $"Reported {TemperatureC}°C for {Station}.";
		}
		catch (Exception ex)
		{
			Status = $"Report failed: {ex.Message}";
		}
	}

	private void OnReadingReported(StationReading reading) => OnUiThread(() =>
	{
		LatestReading = reading;
		Updates.Insert(0, reading);
	});

	/// <summary>
	/// Re-fetch after a reconnect. Anything broadcast while the connection was down is simply
	/// gone, so resuming the stream without re-fetching leaves the UI quietly stale.
	/// </summary>
	private async void OnReconnected()
	{
		try
		{
			var latest = await _stationApi.GetLatestReadingAsync(Station);
			OnUiThread(() =>
			{
				LatestReading = latest;
				Status = "Reconnected.";
			});
		}
		catch (Exception ex)
		{
			OnUiThread(() => Status = $"Reconnected, but could not refresh: {ex.Message}");
		}
	}

	private void OnUiThread(Action action)
	{
		if (_uiContext is null)
		{
			action();
		}
		else
		{
			_uiContext.Post(_ => action(), null);
		}
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~StationsViewModelTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Run the whole test project**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add PublicOtel.ClientLogic/Realtime/StationsViewModel.cs PublicOtel.ClientTests/Realtime/StationsViewModelTests.cs
git commit -m "feat: add StationsViewModel driven by SignalR pushes

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Reqnroll coverage of the realtime flow

Follows the existing `Weather.feature` convention so the generated app keeps one BDD style rather than two.

**Files:**
- Create: `PublicOtel.ClientTests/Features/Stations.feature`
- Create: `PublicOtel.ClientTests/Steps/StationSteps.cs`

**Interfaces:**
- Consumes: `StationsViewModel`, `IStationApiClient`, `IStationHubClient`, `StationReading`.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Write the feature**

Create `PublicOtel.ClientTests/Features/Stations.feature`:

```gherkin
Feature: Live station readings
  A reading reported on one device reaches every other device without anyone pressing refresh.

  Scenario: A reported reading reaches the server
    Given the app is connected to the station hub
    When the user reports 24 degrees for station "north"
    Then the API receives a reading of 24 for station "north"

  Scenario: A broadcast reading appears without a refresh
    Given the app is connected to the station hub
    When the server broadcasts 31 degrees for station "north"
    Then the latest reading shown is 31
    And the update list holds 1 entry

  Scenario: The newest broadcast is shown first
    Given the app is connected to the station hub
    When the server broadcasts 10 degrees for station "north"
    And the server broadcasts 28 degrees for station "north"
    Then the latest reading shown is 28
    And the update list holds 2 entries
```

- [ ] **Step 2: Write the steps**

Create `PublicOtel.ClientTests/Steps/StationSteps.cs` (tabs):

```csharp
using PublicOtel.ClientLogic.Realtime;
using Reqnroll;

namespace PublicOtel.ClientTests.Steps;

[Binding]
public class StationSteps
{
	private readonly IStationApiClient _api = Substitute.For<IStationApiClient>();
	private readonly IStationHubClient _hub = Substitute.For<IStationHubClient>();
	private StationsViewModel? _viewModel;

	private StationsViewModel ViewModel => _viewModel ??= new StationsViewModel(_api, _hub);

	[Given("the app is connected to the station hub")]
	public async Task GivenTheAppIsConnected() => await ViewModel.ConnectCommand.ExecuteAsync(null);

	[When("the user reports {int} degrees for station {string}")]
	public async Task WhenTheUserReports(int temperatureC, string station)
	{
		ViewModel.Station = station;
		ViewModel.TemperatureC = temperatureC;

		await ViewModel.ReportReadingCommand.ExecuteAsync(null);
	}

	[When("the server broadcasts {int} degrees for station {string}")]
	public void WhenTheServerBroadcasts(int temperatureC, string station) =>
		_hub.ReadingReported += Raise.Event<Action<StationReading>>(
			new StationReading(station, temperatureC, DateTimeOffset.UtcNow));

	[Then("the API receives a reading of {int} for station {string}")]
	public async Task ThenTheApiReceivesAReading(int temperatureC, string station) =>
		await _api.Received(1).ReportReadingAsync(station, temperatureC, Arg.Any<CancellationToken>());

	[Then("the latest reading shown is {int}")]
	public void ThenTheLatestReadingShownIs(int temperatureC) =>
		ViewModel.LatestReading!.TemperatureC.ShouldBe(temperatureC);

	[Then("the update list holds {int} entry")]
	[Then("the update list holds {int} entries")]
	public void ThenTheUpdateListHolds(int count) => ViewModel.Updates.Count.ShouldBe(count);
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test PublicOtel.ClientTests/PublicOtel.ClientTests.csproj --filter "FullyQualifiedName~Stations"`
Expected: PASS — the three scenarios plus the `StationsViewModelTests` from Task 8.

- [ ] **Step 4: Commit**

```powershell
git add PublicOtel.ClientTests/Features/Stations.feature PublicOtel.ClientTests/Steps/StationSteps.cs
git commit -m "test: add Reqnroll coverage of the live station flow

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: The MAUI page and the AppShell change

**Files:**
- Modify: `PublicOtel.Mobile/AppShell.xaml.cs` — **unconditional change**
- Modify: `PublicOtel.Mobile/AppShell.xaml`
- Create: `PublicOtel.Mobile/StationsPage.xaml`
- Create: `PublicOtel.Mobile/StationsPage.xaml.cs`
- Modify: `PublicOtel.Mobile/MauiProgram.cs`

**Interfaces:**
- Consumes: `StationsViewModel`, `IStationApiClient`, `StationApiClient`, `IStationHubClient`, `StationHubClient`, `ApiServiceAddress.Resolve`.
- Produces: nothing other tasks depend on.

There is no automated test here. `PublicOtel.Mobile` targets `net10.0-android`, `-ios`, `-maccatalyst` and `-windows`, none of which the CI runner can build, and a UI test needs an emulator. The gate is a clean Windows build plus Task 15's manual run.

- [ ] **Step 1: Switch AppShell to resolving pages from the container**

Replace the whole of `PublicOtel.Mobile/AppShell.xaml.cs` (tabs):

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace PublicOtel.Mobile;

public partial class AppShell : Shell
{
	/// <summary>
	/// Pages are resolved from the container here rather than taken as constructor
	/// parameters. This is the composition root and its only job is assembling pages, so
	/// adding a tab stays a change in two obvious places - the XAML and one line here -
	/// instead of also rewriting this signature.
	/// </summary>
	public AppShell(IServiceProvider services)
	{
		InitializeComponent();

		HomeContent.Content = services.GetRequiredService<MainPage>();
		StationsContent.Content = services.GetRequiredService<StationsPage>();
	}
}
```

- [ ] **Step 2: Add the second tab**

In `PublicOtel.Mobile/AppShell.xaml`, after the existing `ShellContent`, add:

```xml
    <ShellContent
        x:Name="StationsContent"
        Title="Stations"
        Route="StationsPage" />
```

- [ ] **Step 3: Write the page**

Create `PublicOtel.Mobile/StationsPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:rt="clr-namespace:PublicOtel.ClientLogic.Realtime;assembly=PublicOtel.ClientLogic"
             x:Class="PublicOtel.Mobile.StationsPage"
             x:DataType="rt:StationsViewModel"
             Title="Stations">

    <Grid Padding="24"
          RowSpacing="12"
          RowDefinitions="Auto,Auto,Auto,Auto,Auto,*">

        <Label Grid.Row="0"
               Text="Live station readings"
               FontSize="24"
               FontAttributes="Bold"
               SemanticProperties.HeadingLevel="Level1" />

        <HorizontalStackLayout Grid.Row="1" Spacing="10">
            <Label Text="Station" VerticalOptions="Center" />
            <Entry Text="{Binding Station}"
                   WidthRequest="140"
                   SemanticProperties.Hint="The station to report for and listen to" />
            <Label Text="°C" VerticalOptions="Center" />
            <Entry Text="{Binding TemperatureC}"
                   Keyboard="Numeric"
                   WidthRequest="80"
                   SemanticProperties.Hint="The temperature to report" />
        </HorizontalStackLayout>

        <Button Grid.Row="2"
                Text="Connect"
                HeightRequest="48"
                Command="{Binding ConnectCommand}" />

        <Button Grid.Row="3"
                Text="Report Reading"
                HeightRequest="48"
                Command="{Binding ReportReadingCommand}" />

        <Label Grid.Row="4"
               Text="{Binding Status}" />

        <CollectionView Grid.Row="5"
                        ItemsSource="{Binding Updates}">
            <CollectionView.Header>
                <Label Text="Pushed from the server"
                       FontAttributes="Bold"
                       Margin="0,12,0,4" />
            </CollectionView.Header>
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="rt:StationReading">
                    <Border Margin="0,4"
                            Padding="12"
                            StrokeShape="RoundRectangle 10"
                            StrokeThickness="1">
                        <Grid ColumnDefinitions="*,Auto">
                            <VerticalStackLayout Grid.Column="0" Spacing="2">
                                <Label Text="{Binding Station}" FontAttributes="Bold" />
                                <Label Text="{Binding ObservedAt, StringFormat='{0:T}'}"
                                       FontSize="13"
                                       Opacity="0.75" />
                            </VerticalStackLayout>
                            <Label Grid.Column="1"
                                   Text="{Binding TemperatureC, StringFormat='{0}°C'}"
                                   FontSize="22"
                                   VerticalOptions="Center" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

    </Grid>

</ContentPage>
```

Create `PublicOtel.Mobile/StationsPage.xaml.cs` (tabs):

```csharp
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.Mobile;

public partial class StationsPage : ContentPage
{
	public StationsPage(StationsViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = viewModel;
	}
}
```

- [ ] **Step 4: Register everything**

In `PublicOtel.Mobile/MauiProgram.cs`, add this using near the existing `using PublicOtel.ClientLogic;`:

```csharp
using PublicOtel.ClientLogic.Realtime;
```

After the existing `builder.Services.AddHttpClient<IWeatherApiClient, WeatherApiClient>(...)` block, add:

```csharp
		builder.Services.AddHttpClient<IStationApiClient, StationApiClient>(client =>
		{
			client.BaseAddress = new Uri("https+http://apiservice");
		});

		// HubConnection is built directly rather than through HttpClientFactory, so service
		// discovery does not rewrite its address the way it does for the typed clients above.
		// The address is looked up once, here, from the same keys Aspire injects.
		builder.Services.AddSingleton<IStationHubClient>(_ =>
			new StationHubClient(new Uri(
				ApiServiceAddress.Resolve(builder.Configuration), "/hubs/weather")));
```

And alongside the existing `AddTransient<WeatherViewModel>()` / `AddTransient<MainPage>()` lines:

```csharp
		builder.Services.AddTransient<StationsViewModel>();
		builder.Services.AddTransient<StationsPage>();
```

- [ ] **Step 5: Build the Windows target to verify**

Run: `dotnet build PublicOtel.Mobile/PublicOtel.Mobile.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeded, 0 errors.

If the exact Windows TFM differs, read it from `PublicOtel.Mobile/PublicOtel.Mobile.csproj` `<TargetFrameworks>` and use that value.

- [ ] **Step 6: Commit**

```powershell
git add PublicOtel.Mobile
git commit -m "feat: add live Stations page and resolve shell pages from DI

AppShell now takes IServiceProvider instead of each page as a constructor
parameter, so adding a tab does not change the signature.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Port to the template copy with conditional markers

The template content is a hand-maintained copy. This task carries every change from Tasks 1-10 across and wraps the parts that belong to the flag.

**Files:** everything under `PublicOtel.Templates/templates/publicotel-aspire-maui/` that mirrors a file touched in Tasks 1-10.

**Interfaces:**
- Consumes: the finished root solution.
- Produces: a template copy whose conditional regions are ready for the symbol added in Task 12.

- [ ] **Step 1: Copy the new files across verbatim**

```powershell
$src = "."
$dst = "PublicOtel.Templates/templates/publicotel-aspire-maui"

Copy-Item -Recurse -Force "$src/PublicOtel.ApiService/Actors"   "$dst/PublicOtel.ApiService/"
Copy-Item -Recurse -Force "$src/PublicOtel.ApiService/Hubs"     "$dst/PublicOtel.ApiService/"
Copy-Item -Recurse -Force "$src/PublicOtel.ApiService/Telemetry" "$dst/PublicOtel.ApiService/"
Copy-Item -Recurse -Force "$src/PublicOtel.ClientLogic/Realtime" "$dst/PublicOtel.ClientLogic/"
Copy-Item -Recurse -Force "$src/PublicOtel.ClientTests/Actors"  "$dst/PublicOtel.ClientTests/"
Copy-Item -Recurse -Force "$src/PublicOtel.ClientTests/Realtime" "$dst/PublicOtel.ClientTests/"
Copy-Item -Force "$src/PublicOtel.ClientTests/Features/Stations.feature" "$dst/PublicOtel.ClientTests/Features/"
Copy-Item -Force "$src/PublicOtel.ClientTests/Steps/StationSteps.cs"     "$dst/PublicOtel.ClientTests/Steps/"
Copy-Item -Force "$src/PublicOtel.Mobile/StationsPage.xaml"     "$dst/PublicOtel.Mobile/"
Copy-Item -Force "$src/PublicOtel.Mobile/StationsPage.xaml.cs"  "$dst/PublicOtel.Mobile/"
Copy-Item -Force "$src/PublicOtel.Mobile/AppShell.xaml.cs"      "$dst/PublicOtel.Mobile/"
```

`AppShell.xaml.cs` is copied verbatim because its change is unconditional. Every other modified file is edited by hand below, because each needs markers.

- [ ] **Step 2: Wrap the ApiService csproj reference**

In `$dst/PublicOtel.ApiService/PublicOtel.ApiService.csproj`:

```xml
    <!--#if (IncludeAkka) -->
    <PackageReference Include="Akka.Hosting" Version="1.5.71" />
    <!--#endif -->
```

- [ ] **Step 3: Wrap the ApiService Program.cs additions**

In `$dst/PublicOtel.ApiService/Program.cs`, the usings at the top:

```csharp
//#if (IncludeAkka)
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Hubs;
//#endif
```

the service registrations:

```csharp
//#if (IncludeAkka)
// SignalR is what lets the server tell the phone something happened, instead of the phone
// having to keep asking.
builder.Services.AddSignalR();

// The actor system goes in the container alongside everything else.
builder.AddWeatherActors();
//#endif
```

and the endpoint registrations:

```csharp
//#if (IncludeAkka)
app.MapHub<WeatherHub>("/hubs/weather");
app.MapWeatherStationEndpoints();
//#endif
```

- [ ] **Step 4: Wrap the ClientLogic csproj references**

In `$dst/PublicOtel.ClientLogic/PublicOtel.ClientLogic.csproj`:

```xml
    <!--#if (IncludeAkka) -->
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.11" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.11" />
    <!--#endif -->
```

- [ ] **Step 5: Wrap the ClientTests csproj additions**

In `$dst/PublicOtel.ClientTests/PublicOtel.ClientTests.csproj`, the `xunit.v3` bump is **unconditional** — copy the root version of that line, comment and all. The other two are wrapped:

```xml
    <!--#if (IncludeAkka) -->
    <PackageReference Include="Akka.TestKit.Xunit" Version="1.5.71" />
    <!--#endif -->
```

```xml
    <!--#if (IncludeAkka) -->
    <ProjectReference Include="..\PublicOtel.ApiService\PublicOtel.ApiService.csproj" />
    <!--#endif -->
```

- [ ] **Step 6: Wrap the MauiProgram and AppShell additions**

In `$dst/PublicOtel.Mobile/MauiProgram.cs`, wrap the new `using`, the two client registrations, and the two `AddTransient` lines each in their own `//#if (IncludeAkka)` / `//#endif` pair.

In `$dst/PublicOtel.Mobile/AppShell.xaml`:

```xml
    <!--#if (IncludeAkka) -->
    <ShellContent
        x:Name="StationsContent"
        Title="Stations"
        Route="StationsPage" />
    <!--#endif -->
```

In `$dst/PublicOtel.Mobile/AppShell.xaml.cs`, wrap the one page line — the constructor signature stays as copied:

```csharp
		HomeContent.Content = services.GetRequiredService<MainPage>();
//#if (IncludeAkka)
		StationsContent.Content = services.GetRequiredService<StationsPage>();
//#endif
```

- [ ] **Step 7: Verify no build output was copied in**

```powershell
git status --short PublicOtel.Templates
```

Expected: only source files. If any `bin/` or `obj/` path appears, delete it — `.gitignore` should already prevent it, but `Copy-Item -Recurse` is indiscriminate.

- [ ] **Step 8: Commit**

```powershell
git add PublicOtel.Templates
git commit -m "feat: port the Akka and SignalR feature into the template copy

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: The template symbol

**Files:**
- Modify: `PublicOtel.Templates/templates/publicotel-aspire-maui/.template.config/template.json`
- Modify: `.../.template.config/dotnetcli.host.json`
- Modify: `.../.template.config/ide.host.json`

**Interfaces:**
- Consumes: the markers from Task 11.
- Produces: the `--include-akka` CLI option.

- [ ] **Step 1: Add the symbol**

In `template.json`, add to `symbols`, after the `hostIdentifier` entry:

```json
    "IncludeAkka": {
      "type": "parameter",
      "datatype": "bool",
      "defaultValue": "false",
      "displayName": "Include Akka.NET actors and SignalR",
      "description": "Adds an Akka.NET actor system to the API service via Akka.Hosting, a message envelope carrying ActivityContext so traces survive the actor mailbox, and a SignalR hub that pushes actor state changes live to the MAUI client."
    },
```

- [ ] **Step 2: Exclude the flag-off content**

In `template.json`, add a second entry to `sources[0].modifiers`, after the existing `hostIdentifier` modifier:

```json
        {
          "condition": "(!IncludeAkka)",
          "exclude": [
            "PublicOtel.ApiService/Actors/**",
            "PublicOtel.ApiService/Hubs/**",
            "PublicOtel.ApiService/Telemetry/**",
            "PublicOtel.ClientLogic/Realtime/**",
            "PublicOtel.ClientTests/Actors/**",
            "PublicOtel.ClientTests/Realtime/**",
            "PublicOtel.ClientTests/Features/Stations.feature",
            "PublicOtel.ClientTests/Steps/StationSteps.cs",
            "PublicOtel.Mobile/StationsPage.xaml",
            "PublicOtel.Mobile/StationsPage.xaml.cs"
          ]
        }
```

- [ ] **Step 3: Give it a kebab-case CLI name**

In `dotnetcli.host.json`, add to `symbolInfo`:

```json
    "IncludeAkka": {
      "longName": "include-akka",
      "shortName": ""
    },
```

Without this the CLI exposes it as `--IncludeAkka`.

- [ ] **Step 4: Show it in the Visual Studio dialog**

In `ide.host.json`, add to the `symbolInfo` array:

```json
    {
      "id": "IncludeAkka",
      "isVisible": true
    }
```

- [ ] **Step 5: Validate both variants locally**

```powershell
dotnet pack PublicOtel.Templates/PublicOtel.Templates.csproj -c Release
dotnet new uninstall PublicOtel.Templates
dotnet new install PublicOtel.Templates/bin/Release/PublicOtel.Templates.1.8.0.nupkg

$smoke = Join-Path $env:TEMP "publicotel-smoke"
Remove-Item -Recurse -Force $smoke -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $smoke | Out-Null
Push-Location $smoke

dotnet new publicotel-maui -n Plain.Check
dotnet new publicotel-maui -n Akka.Check --include-akka

Pop-Location
```

Expected: both succeed, and `dotnet new publicotel-maui --help` lists `--include-akka`.

- [ ] **Step 6: Check the generated output**

```powershell
$smoke = Join-Path $env:TEMP "publicotel-smoke"

# The flag-off app must have none of the new files.
Get-ChildItem -Recurse "$smoke/Plain.Check" -Include "StationsPage.xaml","Stations.feature","WeatherStationActor.cs"

# Neither app may contain a surviving marker or token.
Get-ChildItem -Recurse "$smoke/Plain.Check","$smoke/Akka.Check" -Include *.cs,*.csproj,*.xaml,*.sln,*.json |
    Select-String -Pattern "IncludeAkka","#endif","PublicOtel","APPIDNAME","TUNNELSUFFIX"
```

Expected: **no output from either command.**

Note `#endif` here would be a template marker left behind. The genuine `#if DEBUG` in `MauiProgram.cs` pairs with an `#endif`, so if that one line appears, confirm it is the `DEBUG` block and nothing else.

- [ ] **Step 7: Build the generated flag-on app's testable projects**

```powershell
$smoke = Join-Path $env:TEMP "publicotel-smoke"
dotnet test "$smoke/Akka.Check/Akka.Check.ClientTests/Akka.Check.ClientTests.csproj"
dotnet test "$smoke/Plain.Check/Plain.Check.ClientTests/Plain.Check.ClientTests.csproj"
```

Expected: both PASS. The flag-on run includes the actor and realtime tests; the flag-off run does not.

- [ ] **Step 8: Commit**

```powershell
git add PublicOtel.Templates
git commit -m "feat: add the --include-akka template option

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 13: CI covers both variants

**Files:**
- Modify: `.github/workflows/pack.yml`

**Interfaces:**
- Consumes: the `--include-akka` option from Task 12.
- Produces: CI that fails if either variant breaks.

- [ ] **Step 1: Replace the smoke-test step**

Replace the whole `- name: Smoke test the generated template` step with:

```yaml
      # Catches the failure mode that actually bites: a template that packs cleanly but
      # generates an app that does not compile, because a token, GUID or project reference
      # did not survive substitution. Both variants are generated, because a regression in
      # the --include-akka=false path would otherwise ship unnoticed.
      - name: Smoke test the generated template
        run: |
          set -euo pipefail
          dotnet new install ./artifacts/PublicOtel.Templates.${{ steps.pack.outputs.version }}.nupkg
          mkdir -p /tmp/smoke && cd /tmp/smoke
          dotnet new publicotel-maui -n Ci.Smoke
          dotnet new publicotel-maui -n Ci.SmokeAkka --include-akka

          echo "--- no template tokens or conditional markers should survive ---"
          if grep -rn "PublicOtel\|APPIDNAME\|TUNNELSUFFIX\|GeneratedClassNamePrefix\|IncludeAkka" \
               Ci.Smoke Ci.SmokeAkka \
               --include=*.cs --include=*.csproj --include=*.xaml --include=*.sln \
               --include=*.json --include=*.ps1 --include=*.feature; then
            echo "::error::Unsubstituted template tokens found in a generated app"
            exit 1
          fi

          echo "--- the akka content must be absent without the flag ---"
          if find Ci.Smoke -name 'StationsPage.xaml' -o -name 'Stations.feature' \
                           -o -name 'WeatherStationActor.cs' | grep .; then
            echo "::error::Akka content leaked into the app generated without --include-akka"
            exit 1
          fi

          echo "--- and present with it ---"
          test -f Ci.SmokeAkka/Ci.SmokeAkka.ApiService/Actors/WeatherStationActor.cs
          test -f Ci.SmokeAkka/Ci.SmokeAkka.Mobile/StationsPage.xaml

          echo "--- the generated cross-platform projects must build and pass, both ways ---"
          dotnet test Ci.Smoke/Ci.Smoke.ClientTests/Ci.Smoke.ClientTests.csproj --configuration Release
          dotnet test Ci.SmokeAkka/Ci.SmokeAkka.ClientTests/Ci.SmokeAkka.ClientTests.csproj --configuration Release

          echo "--- the actor and hub code must compile on its own ---"
          dotnet build Ci.SmokeAkka/Ci.SmokeAkka.ApiService/Ci.SmokeAkka.ApiService.csproj \
            --configuration Release
```

- [ ] **Step 2: Commit**

```powershell
git add .github/workflows/pack.yml
git commit -m "ci: smoke test the template with and without --include-akka

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 14: Documentation and version bump

**Files:**
- Modify: `PublicOtel.Templates/README.md`
- Modify: `README.md`
- Modify: `PublicOtel.Templates/PublicOtel.Templates.csproj`

**Interfaces:**
- Consumes: everything above.
- Produces: a release-ready pack at 1.9.0.

- [ ] **Step 1: Add the option to the template README's options table**

In `PublicOtel.Templates/README.md`, under `## Use`, add a row to the existing table:

```markdown
| `--include-akka` | `false` | Adds an Akka.NET actor system to the API, a message envelope that carries `ActivityContext` across the actor mailbox, and a SignalR hub pushing live updates to a second page in the MAUI app. |
```

- [ ] **Step 2: Add a section explaining the option**

In `PublicOtel.Templates/README.md`, after the `## Use` section, add:

```markdown
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

**SignalR.** `WeatherHub` at `/hubs/weather` broadcasts on every state change; the MAUI app's
**Stations** page subscribes and updates live. Reports go over HTTP rather than a hub method,
deliberately: an HTTP POST carries `traceparent` and starts inside a trace, a hub invocation
does not.

| Endpoint | Actor call | Response |
|---|---|---|
| `POST /stations/{station}/readings` | `Tell` | `202 Accepted` — the actor may not have processed it yet |
| `GET /stations/{station}` | `Ask` (3s timeout) | `200` with the reading, or `404` |

### The two-device demo

Start the app, open the **Stations** page on two devices (two emulators, or Windows plus an
emulator), press **Connect** on both, then **Report Reading** on one. The other updates with
no refresh.

Android reaches the API over the existing `mobile-api` dev tunnel, and SignalR therefore rides
that tunnel too. Dev tunnels do carry WebSockets, but it is a new failure surface: if live
updates work on Windows and not on Android, check the tunnel before suspecting the hub.
```

- [ ] **Step 3: Extend the maintenance notes**

In `PublicOtel.Templates/README.md`, under `## Maintaining the template`, add a bullet to the
existing list of things that deliberately differ between the working copy and the template:

```markdown
- The `//#if (IncludeAkka)` and `<!--#if (IncludeAkka) -->` markers in `Program.cs`,
  `MauiProgram.cs`, `AppShell.xaml`, `AppShell.xaml.cs` and three `.csproj` files. The working
  copy has that code unconditionally; only the template copy carries markers. Whole files
  under `Actors/`, `Hubs/`, `Telemetry/` and `Realtime/` need no markers — `template.json`
  excludes them by path.
```

- [ ] **Step 4: Mention the option in the root README**

In `README.md`, add a short paragraph near the existing template usage notes:

```markdown
### Akka.NET and SignalR

`dotnet new publicotel-maui -n Your.App --include-akka` adds an Akka.NET actor system behind
the API, a message envelope that carries `ActivityContext` so a trace survives the actor
mailbox, and a SignalR hub that pushes state changes to a live **Stations** page in the MAUI
app. See `PublicOtel.Templates/README.md` for what it generates and how to run the two-device
demo.
```

- [ ] **Step 5: Bump the pack version**

In `PublicOtel.Templates/PublicOtel.Templates.csproj`:

```xml
    <Version>1.9.0</Version>
```

- [ ] **Step 6: Commit**

```powershell
git add README.md PublicOtel.Templates/README.md PublicOtel.Templates/PublicOtel.Templates.csproj
git commit -m "docs: document --include-akka and bump the pack to 1.9.0

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 15: Manual acceptance

Acceptance criteria 3 and 4 in the spec cannot be automated here: one needs the Aspire
dashboard's trace waterfall, the other needs two running clients.

**Files:** none.

- [ ] **Step 1: Run the app**

Run: `aspire start`
Expected: the dashboard opens with `apiservice`, `webfrontend` and the mobile resources.

- [ ] **Step 2: Start two clients**

Start the `mobile-windows` resource. Then run `./scripts/run-android.ps1` for a second client,
or start a second Windows instance. Open the **Stations** page on both and press **Connect**.

- [ ] **Step 3: Verify the live update**

Press **Report Reading** on one client.
Expected: the other client's latest reading and update list change **without a refresh**.

- [ ] **Step 4: Verify the trace**

In the dashboard, open **Traces** and find the trace for the report.
Expected: one trace containing the mobile client span, the `POST /stations/{station}/readings`
server span beneath it, and `WeatherStationActor.ReportReading` beneath *that*. **No orphan
spans.** If the actor span appears as a separate root, `parentContext:` is not being passed.

- [ ] **Step 5: Verify the teaching break still works**

Temporarily delete `parentContext: message.TraceContext` from `WeatherStationActor.HandleReportAsync`,
re-run, and confirm the actor span now appears as a parentless root sharing the same trace id.
Then restore it. This is the behaviour the README promises; it should be confirmed once rather
than assumed.

- [ ] **Step 6: Report results**

Report what was observed for Steps 3, 4 and 5. If anything failed, stop and report rather than
patching — a failure here means one of Tasks 5, 10 or 11 is wrong.

---

## Self-Review

**Spec coverage.** Section 1 (the flag) → Task 12. Section 2 (API side) → Tasks 2-5. Section 3
(client side) → Tasks 6-8, 10. Section 4 (repo layout) → Tasks 1-10 build the root solution,
Task 11 ports it. Section 5 (conditional edits list) → Task 11, all seven rows plus the two
unconditional changes. Section 6 (tests) → Tasks 1-4, 6, 8, 9. Section 7 (CI) → Task 13.
Section 8 (docs and version) → Task 14. Section 9 (acceptance criteria) → criteria 1, 2 and 5
in Task 12 Steps 5-7, criterion 6 in Task 13, criteria 3 and 4 in Task 15.

**One addition to the spec.** The spec's exclusion list omitted `PublicOtel.ClientTests/Actors/**`,
which is where the TestKit tests live. Task 12 Step 2 includes it.

**Type consistency.** `StationReading` is declared twice on purpose — `PublicOtel.ApiService.Actors.StationReading`
and `PublicOtel.ClientLogic.Realtime.StationReading` — with identical shapes and no shared
project. Both are documented as deliberate. `CreateProps` is the factory name on both actors.
`ReadingReported` is the method name on `IWeatherClient`, the SignalR method-name constant in
`StationHubClient`, and the event on `IStationHubClient`; all three must stay in sync and the
constant says so.
