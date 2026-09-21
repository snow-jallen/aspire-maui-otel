using System.Diagnostics;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// One actor per weather station, owning that station's latest reading.
/// </summary>
/// <remarks>
/// <para>
/// The contract: <c>_latest</c> is private, the mailbox delivers one message at a time, and
/// nothing else in the process can touch that field. That is why there is no lock here and
/// why there does not need to be one.
/// </para>
/// <para>
/// One actor per station rather than one actor holding a dictionary of stations: two stations
/// reporting at the same moment are then genuinely concurrent, because they are different
/// mailboxes. A single actor with a dictionary would serialise the whole system through one
/// queue.
/// </para>
/// </remarks>
public sealed class WeatherStationActor : ReceiveActor
{
    private const int MinTemperatureC = -90;
    private const int MaxTemperatureC = 60;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;

    private StationReading? _latest;

    public WeatherStationActor(string station, IHubContext<WeatherHub, IWeatherClient> hub)
    {
        _hub = hub;

        _log.Debug("Station actor for {0} started.", station);

        ReceiveAsync<ReportReading>(HandleReportAsync);
        Receive<GetLatestReading>(HandleGet);
    }

    public static Props CreateProps(string station, IHubContext<WeatherHub, IWeatherClient> hub) =>
        Props.Create(() => new WeatherStationActor(station, hub));

    private async Task HandleReportAsync(ReportReading message)
    {
        // parentContext is the entire fix. Delete that one argument and this span becomes a
        // root: same trace id, no parent, floating next to the API span in the dashboard
        // instead of underneath it. That is the broken waterfall from the Monday lecture, and
        // reproducing it really is this cheap.
        using var activity = ApiTelemetry.Source.StartActivity(
            "WeatherStationActor.ReportReading",
            ActivityKind.Consumer,
            parentContext: message.TraceContext);

        activity?.SetTag("station.id", message.Station);
        activity?.SetTag("station.temperature_c", message.TemperatureC);

        if (message.TemperatureC is < MinTemperatureC or > MaxTemperatureC)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Reading out of range.");

            // Throwing inside an actor is not the same as throwing inside a request handler.
            // Nobody catches this; the mailbox stops, the parent decides what happens next.
            throw new InvalidReadingException(message.Station, message.TemperatureC);
        }

        _latest = new StationReading(message.Station, message.TemperatureC, DateTimeOffset.UtcNow);
        _log.Info("Station {0} is now {1}°C.", message.Station, message.TemperatureC);

        await _hub.Clients.All.ReadingReported(_latest);
    }

    private void HandleGet(GetLatestReading message)
    {
        using var activity = ApiTelemetry.Source.StartActivity(
            "WeatherStationActor.GetLatestReading",
            ActivityKind.Consumer,
            parentContext: message.TraceContext);

        activity?.SetTag("station.id", message.Station);
        activity?.SetTag("station.has_reading", _latest is not null);

        // Sender is whoever sent this message. For an Ask that is a temporary actor the Ask
        // created, and replying to it is what completes the caller's Task.
        Sender.Tell(_latest is null ? new NoReadingYet(message.Station) : _latest);
    }
}
