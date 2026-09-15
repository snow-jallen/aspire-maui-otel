using System.Net.Http.Json;

namespace PublicOtel.ClientLogic;

/// <summary>
/// Abstraction over the weather endpoint so view models can be unit tested with a
/// substitute instead of a real HttpClient.
/// </summary>
public interface IWeatherApiClient
{
	Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default);
}

public class WeatherApiClient(HttpClient httpClient) : IWeatherApiClient
{
	public async Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
	{
		List<WeatherForecast>? forecasts = null;

		await foreach (var forecast in httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/weatherforecast", cancellationToken))
		{
			if (forecasts?.Count >= maxItems)
			{
				break;
			}
			if (forecast is not null)
			{
				forecasts ??= [];
				forecasts.Add(forecast);
			}
		}

		return forecasts?.ToArray() ?? [];
	}
}

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
	public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
