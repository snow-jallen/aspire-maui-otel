using PublicOtel.ClientLogic;

namespace PublicOtel.ClientTests;

/// <summary>
/// Plain unit tests for the view model, using NSubstitute for the API client and Shouldly
/// for assertions. No emulator, no AppHost, no network.
/// </summary>
public class WeatherViewModelTests
{
	private static WeatherForecast Forecast(int temperatureC = 20, string summary = "Mild") =>
		new(DateOnly.FromDateTime(DateTime.Today), temperatureC, summary);

	[Fact]
	public async Task LoadWeather_populates_forecasts_from_the_api()
	{
		var api = Substitute.For<IWeatherApiClient>();
		api.GetWeatherAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
		   .Returns([Forecast(12, "Chilly"), Forecast(24, "Balmy")]);

		var viewModel = new WeatherViewModel(api, new Telemetry());

		await viewModel.LoadWeatherCommand.ExecuteAsync(null);

		viewModel.Forecasts.Count.ShouldBe(2);
		viewModel.Forecasts[0].Summary.ShouldBe("Chilly");
		viewModel.Status.ShouldContain("2 forecasts");
		viewModel.IsBusy.ShouldBeFalse();
	}

	[Fact]
	public async Task LoadWeather_clears_previous_results_before_reloading()
	{
		var api = Substitute.For<IWeatherApiClient>();
		api.GetWeatherAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
		   .Returns([Forecast(), Forecast()], [Forecast()]);

		var viewModel = new WeatherViewModel(api, new Telemetry());

		await viewModel.LoadWeatherCommand.ExecuteAsync(null);
		await viewModel.LoadWeatherCommand.ExecuteAsync(null);

		viewModel.Forecasts.Count.ShouldBe(1);
	}

	[Fact]
	public async Task LoadWeather_reports_failure_without_throwing()
	{
		var api = Substitute.For<IWeatherApiClient>();
		api.GetWeatherAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
		   .Returns<WeatherForecast[]>(_ => throw new HttpRequestException("apiservice unreachable"));

		var viewModel = new WeatherViewModel(api, new Telemetry());

		await viewModel.LoadWeatherCommand.ExecuteAsync(null);

		viewModel.Status.ShouldContain("apiservice unreachable");
		viewModel.Forecasts.ShouldBeEmpty();
		viewModel.IsBusy.ShouldBeFalse();
	}

	[Fact]
	public void TemperatureF_converts_from_celsius()
	{
		Forecast(0).TemperatureF.ShouldBe(32);
		Forecast(100).TemperatureF.ShouldBe(211);
	}
}
