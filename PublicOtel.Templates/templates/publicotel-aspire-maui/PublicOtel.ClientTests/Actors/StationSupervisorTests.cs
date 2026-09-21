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

	[Fact]
	public void A_station_id_that_is_not_a_bare_identifier_still_gets_an_actor()
	{
		var supervisor = Supervisor();

		// Escapes to "north%20side". Akka accepts percent-encoded path elements, and
		// EscapeDataString is injective, so distinct ids never collide onto one actor.
		supervisor.Tell(new ReportReading("north side", 21, default));
		supervisor.Tell(new GetLatestReading("north side", default));

		ExpectMsg<StationReading>().TemperatureC.ShouldBe(21);
	}

	[Fact]
	public void Two_similar_station_ids_get_separate_actors()
	{
		var supervisor = Supervisor();

		supervisor.Tell(new ReportReading("north side", 21, default));
		supervisor.Tell(new ReportReading("north-side", 30, default));

		supervisor.Tell(new GetLatestReading("north side", default));
		ExpectMsg<StationReading>().TemperatureC.ShouldBe(21);

		supervisor.Tell(new GetLatestReading("north-side", default));
		ExpectMsg<StationReading>().TemperatureC.ShouldBe(30);
	}
}
