using PublicOtel.ClientLogic;
using Reqnroll;

namespace PublicOtel.ClientTests.Steps;

[Binding]
public class WeatherSteps
{
	private readonly IWeatherApiClient _api = Substitute.For<IWeatherApiClient>();
	private WeatherViewModel? _viewModel;

	// Fully qualified on purpose. A bare `Telemetry` fails with CS0118 in any app whose name
	// ends in ".Telemetry" - C# resolves the name to the enclosing namespace instead of this
	// class. Same reason the actor tests spell out Akka.TestKit.Xunit.TestKit.
	private WeatherViewModel ViewModel => _viewModel ??= new WeatherViewModel(_api, new PublicOtel.ClientLogic.Telemetry());

	[Given("the API returns {int} forecasts")]
	public void GivenTheApiReturnsForecasts(int count)
	{
		var forecasts = Enumerable.Range(1, count)
			.Select(i => new WeatherForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(i)), 15 + i, "Mild"))
			.ToArray();

		_api.GetWeatherAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(forecasts);
	}

	[Given("the API is unavailable")]
	public void GivenTheApiIsUnavailable() =>
		_api.GetWeatherAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<WeatherForecast[]>(_ => throw new HttpRequestException("apiservice unreachable"));

	[When("the user asks for the weather")]
	public async Task WhenTheUserAsksForTheWeather() =>
		await ViewModel.LoadWeatherCommand.ExecuteAsync(null);

	[Then("{int} forecasts are shown")]
	public void ThenForecastsAreShown(int count) => ViewModel.Forecasts.Count.ShouldBe(count);

	[Then("no forecasts are shown")]
	public void ThenNoForecastsAreShown() => ViewModel.Forecasts.ShouldBeEmpty();

	[Then("the status mentions {int} forecasts")]
	public void ThenTheStatusMentionsForecasts(int count) =>
		ViewModel.Status.ShouldContain($"{count} forecasts");

	[Then("the status mentions a failure")]
	public void ThenTheStatusMentionsAFailure() => ViewModel.Status.ShouldContain("failed");
}
