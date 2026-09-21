using Microsoft.Extensions.Hosting;
using PublicOtel.ClientLogic;
//#if (IncludeAkka)
using PublicOtel.ClientLogic.Realtime;
//#endif
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

//#if (IncludeAkka)
		builder.Services.AddHttpClient<IStationApiClient, StationApiClient>(client =>
		{
			client.BaseAddress = new Uri("https+http://apiservice");
		});
//#endif

//#if (IncludeAkka)
		// HubConnection is built directly rather than through HttpClientFactory, so service
		// discovery does not rewrite its address the way it does for the typed clients above.
		// The factory is deliberately deferred: resolving the address throws when the app was
		// not launched by the AppHost, and this singleton is resolved at startup, so resolving
		// eagerly here would stop the whole app launching instead of just the Stations page.
		builder.Services.AddSingleton<IStationHubClient>(_ =>
			new StationHubClient(() =>
				new Uri(ApiServiceAddress.Resolve(builder.Configuration), "/hubs/weather")));
//#endif

		builder.Services.AddTransient<WeatherViewModel>();

//#if (IncludeAkka)
		// One StationsViewModel exists for the process lifetime, because AppShell is a singleton
		// that resolves StationsPage exactly once. It subscribes to the hub client's events and
		// never unsubscribes; that is only safe while this stays true.
		builder.Services.AddTransient<StationsViewModel>();
//#endif
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<MainPage>();
//#if (IncludeAkka)
		builder.Services.AddTransient<StationsPage>();
//#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
