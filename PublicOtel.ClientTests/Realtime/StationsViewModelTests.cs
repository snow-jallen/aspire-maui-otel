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
