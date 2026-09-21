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
