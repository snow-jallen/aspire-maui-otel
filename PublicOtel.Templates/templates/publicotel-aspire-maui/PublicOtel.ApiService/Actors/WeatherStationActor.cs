using System.Diagnostics;
using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
/// <para>
/// Two loggers, on purpose. <c>_logger</c> is an ordinary <see cref="ILogger"/>: it writes
/// synchronously, on this thread, while the actor's span is <c>Activity.Current</c>, so the
/// OpenTelemetry logging provider stamps each record with this span's trace id and the log
/// shows up inside the request's trace in the dashboard. <c>_log</c> is Akka's own adapter:
/// it hands the message to Akka's logging actor, which writes it later, on another thread,
/// with no current Activity - so its records reach the dashboard (see
/// <c>ConfigureLoggers</c> in <see cref="WeatherActorExtensions"/>) but belong to no trace.
/// Use <c>_logger</c> for anything that happens while handling a message.
/// </para>
/// </remarks>
public sealed class WeatherStationActor : ReceiveActor
{
    private const int MinTemperatureC = -90;
    private const int MaxTemperatureC = 60;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;
    private readonly ILogger<WeatherStationActor> _logger;

    private StationReading? _latest;

    public WeatherStationActor(
        string station,
        IHubContext<WeatherHub, IWeatherClient> hub,
        ILogger<WeatherStationActor> logger)
    {
        _hub = hub;
        _logger = logger;

        _log.Debug("Station actor for {0} started.", station);

        ReceiveAsync<ReportReading>(HandleReportAsync);
        Receive<GetLatestReading>(HandleGet);
    }

    public static Props CreateProps(
        string station,
        IHubContext<WeatherHub, IWeatherClient> hub,
        ILogger<WeatherStationActor> logger) =>
        Props.Create(() => new WeatherStationActor(station, hub, logger));

    private async Task HandleReportAsync(ReportReading message)
    {
        // parentContext is the entire fix, and the failure without it is worse than it looks.
        // Delete that one argument and StartActivity falls back to Activity.Current, which is
        // null here: the mailbox already broke the ambient flow, because the actor runs on a
        // dispatcher thread long after Tell returned. So this span does not merely lose its
        // parent - it starts a whole new trace with its own trace id, and the actor's work
        // vanishes from the request's waterfall instead of sitting beside it. Measured, not
        // assumed: removing the argument moved this span to an unrelated trace id.
        using var activity = ApiTelemetry.Source.StartActivity(
            "WeatherStationActor.ReportReading",
            ActivityKind.Consumer,
            parentContext: message.TraceContext);

        activity?.SetTag("station.id", message.Station);
        activity?.SetTag("station.temperature_c", message.TemperatureC);

        if (message.TemperatureC is < MinTemperatureC or > MaxTemperatureC)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Reading out of range.");
            _logger.LogWarning(
                "Rejected {TemperatureC}°C from station {Station}; outside {Min}..{Max}°C.",
                message.TemperatureC, message.Station, MinTemperatureC, MaxTemperatureC);

            // Throwing inside an actor is not the same as throwing inside a request handler.
            // Nobody catches this; the mailbox stops, the parent decides what happens next.
            throw new InvalidReadingException(message.Station, message.TemperatureC);
        }

        _latest = new StationReading(message.Station, message.TemperatureC, DateTimeOffset.UtcNow);
        _logger.LogInformation(
            "Station {Station} is now {TemperatureC}°C.", message.Station, message.TemperatureC);

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
