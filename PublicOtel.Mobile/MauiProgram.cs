using Microsoft.Extensions.Hosting;
using PublicOtel.ClientLogic;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace PublicOtel.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.AddServiceDefaults();

		// Register the app's own ActivitySource and Meter with OpenTelemetry. Without these two
		// lines the spans and instruments from Telemetry.cs are created but never exported.
		builder.Services.AddOpenTelemetry()
			.WithTracing(tracing => tracing.AddSource(Telemetry.ActivitySourceName))
			.WithMetrics(metrics => metrics.AddMeter(Telemetry.MeterName));

		builder.Services.AddSingleton<Telemetry>();

		builder.Services.AddHttpClient<IWeatherApiClient, WeatherApiClient>(client =>
		{
			// "https+http://" prefers HTTPS. "apiservice" is the resource name from AppHost.cs,
			// resolved by service discovery from the environment Aspire injects.
			client.BaseAddress = new Uri("https+http://apiservice");
		});

		builder.Services.AddTransient<WeatherViewModel>();
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
