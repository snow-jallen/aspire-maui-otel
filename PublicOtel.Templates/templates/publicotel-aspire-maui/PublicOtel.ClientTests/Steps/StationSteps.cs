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
