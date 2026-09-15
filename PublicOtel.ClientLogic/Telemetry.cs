using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PublicOtel.ClientLogic;

/// <summary>
/// Owns the app's own <see cref="System.Diagnostics.ActivitySource"/> and instruments.
/// Both names are registered with OpenTelemetry in <see cref="MauiProgram"/>, which is what
/// makes the spans and metrics below show up in the Aspire dashboard.
/// </summary>
public sealed class Telemetry : IDisposable
{
	public const string ActivitySourceName = "PublicOtel.Mobile";
	public const string MeterName = "PublicOtel.Mobile";

	private readonly Meter _meter = new(MeterName);

	public Telemetry()
	{
		WeatherRequests = _meter.CreateCounter<long>(
			"publicotel.mobile.weather_requests",
			unit: "{request}",
			description: "Number of weather requests made from the mobile app.");

		WeatherRequestDuration = _meter.CreateHistogram<double>(
			"publicotel.mobile.weather_request.duration",
			unit: "ms",
			description: "How long a weather request took, end to end.");
	}

	public ActivitySource ActivitySource { get; } = new(ActivitySourceName);

	public Counter<long> WeatherRequests { get; }

	public Histogram<double> WeatherRequestDuration { get; }

	public void Dispose()
	{
		ActivitySource.Dispose();
		_meter.Dispose();
	}
}
