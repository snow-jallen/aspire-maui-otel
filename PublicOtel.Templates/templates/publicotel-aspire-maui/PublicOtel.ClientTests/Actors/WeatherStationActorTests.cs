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

		started.ShouldContain(a => a.OperationName == "WeatherStationActor.ReportReading");
		var actorSpan = started.Single(a => a.OperationName == "WeatherStationActor.ReportReading");
		actorSpan.ParentSpanId.ShouldBe(callerContext.SpanId);
		actorSpan.TraceId.ShouldBe(callerContext.TraceId);
	}
}
