using System.Diagnostics;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Hubs;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ClientTests.Actors;

/// <summary>
/// The actor is the point of the whole feature, so it gets real tests: state survives across
/// messages, Ask answers before any state exists, and the span it starts is parented to the
/// context that arrived in the message rather than floating free.
/// </summary>
// Fully qualified on purpose. Written as a bare `TestKit`, this breaks with CS0118 in any
// app whose root namespace starts with "Akka." - C# then resolves the name to the sibling
// Akka.TestKit namespace instead of this class. That is a likely project name for a
// template whose headline feature is Akka.NET.
public class WeatherStationActorTests : Akka.TestKit.Xunit.TestKit
{
	private readonly IHubContext<WeatherHub, IWeatherClient> _hub =
		Substitute.For<IHubContext<WeatherHub, IWeatherClient>>();

	private readonly IWeatherClient _allClients = Substitute.For<IWeatherClient>();

	private readonly CapturingLogger<WeatherStationActor> _logger = new();

	public WeatherStationActorTests()
	{
		var clients = Substitute.For<IHubClients<IWeatherClient>>();
		clients.All.Returns(_allClients);
		_hub.Clients.Returns(clients);
	}

	private IActorRef StationActor(string station = "north") =>
		Sys.ActorOf(WeatherStationActor.CreateProps(station, _hub, _logger));

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

		started.ShouldContain(a => a.OperationName == "WeatherStationActor.ReportReading"
		                        && a.TraceId == callerContext.TraceId);
		var actorSpan = started.Single(a => a.OperationName == "WeatherStationActor.ReportReading"
		                                 && a.TraceId == callerContext.TraceId);
		actorSpan.ParentSpanId.ShouldBe(callerContext.SpanId);
		actorSpan.TraceId.ShouldBe(callerContext.TraceId);
	}

	[Fact]
	public void A_log_written_while_handling_a_report_belongs_to_the_actor_span()
	{
		using var listener = ListenToApiSpans();
		var callerContext = CallerContext();

		var actor = StationActor();
		actor.Tell(new ReportReading("north", 21, callerContext));
		actor.Tell(new GetLatestReading("north", callerContext));
		ExpectMsg<StationReading>();

		// The logger records Activity.Current at the moment of the call, which is exactly
		// what the OpenTelemetry logging provider stamps onto the exported log record. If
		// it is the actor span, the dashboard shows this log inside the request's trace.
		var entry = _logger.Entries.Single(e => e.Message.Contains("north"));
		entry.Level.ShouldBe(LogLevel.Information);
		entry.Activity.ShouldNotBeNull();
		entry.Activity.OperationName.ShouldBe("WeatherStationActor.ReportReading");
		entry.Activity.TraceId.ShouldBe(callerContext.TraceId);
	}

	[Fact]
	public void An_out_of_range_reading_is_logged_inside_the_trace_before_the_actor_fails()
	{
		using var listener = ListenToApiSpans();
		var callerContext = CallerContext();

		var actor = StationActor();
		EventFilter.Exception<InvalidReadingException>().ExpectOne(() =>
		{
			actor.Tell(new ReportReading("north", 5000, callerContext));
		});

		// Akka logs the exception too, but from its logging actor, long after the span has
		// ended - so that copy is not attached to any trace. This one is.
		var entry = _logger.Entries.Single(e => e.Level == LogLevel.Warning);
		entry.Activity.ShouldNotBeNull();
		entry.Activity.TraceId.ShouldBe(callerContext.TraceId);
	}

	private static ActivityListener ListenToApiSpans()
	{
		var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == ApiTelemetry.ActivitySourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
		};
		ActivitySource.AddActivityListener(listener);
		return listener;
	}

	private static ActivityContext CallerContext()
	{
		using var caller = ApiTelemetry.Source.StartActivity("caller");
		return TraceEnvelope.Current();
	}
}

/// <summary>
/// Records each log call together with the <see cref="Activity"/> that was current when it
/// was made.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
	public sealed record Entry(LogLevel Level, string Message, Activity? Activity);

	private readonly List<Entry> _entries = [];

	// Written from the actor's dispatcher thread, read from the test thread.
	public IReadOnlyList<Entry> Entries
	{
		get { lock (_entries) { return [.. _entries]; } }
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		lock (_entries)
		{
			_entries.Add(new Entry(logLevel, formatter(state, exception), Activity.Current));
		}
	}
}
