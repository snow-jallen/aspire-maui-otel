using Akka.Actor;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Parent of one <see cref="WeatherStationActor"/> per station id - the actor-per-entity
/// pattern - and the place where failure decisions are made.
/// </summary>
public sealed class StationSupervisor : ReceiveActor
{
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;
    private readonly ILoggerFactory _loggerFactory;

    public StationSupervisor(IHubContext<WeatherHub, IWeatherClient> hub, ILoggerFactory loggerFactory)
    {
        _hub = hub;
        _loggerFactory = loggerFactory;

        // Forward, not Tell: Forward preserves the original Sender, so an Ask that arrives
        // here gets its reply straight from the child. Tell would make the supervisor the
        // sender and the caller's Ask would time out.
        Receive<ReportReading>(message => ChildFor(message.Station).Forward(message));
        Receive<GetLatestReading>(message => ChildFor(message.Station).Forward(message));
    }

    public static Props CreateProps(IHubContext<WeatherHub, IWeatherClient> hub, ILoggerFactory loggerFactory) =>
        Props.Create(() => new StationSupervisor(hub, loggerFactory));

    /// <summary>
    /// What this actor does when one of its children throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is supervision: the failure is not handled where it happened, it is handled by the
    /// parent, which is the only party that knows what the child is for.
    /// </para>
    /// <para>
    /// <c>Restart</c> replaces the failed child with a fresh instance, so its <c>_latest</c>
    /// is gone. That is a real cost and it is chosen deliberately: a station that produced an
    /// impossible reading has no state worth keeping. <c>Escalate</c> hands anything
    /// unrecognised to this actor's own parent rather than guessing at a policy for it.
    /// </para>
    /// <para>
    /// OneForOne, not AllForOne: one station misbehaving says nothing about the others.
    /// </para>
    /// </remarks>
    protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(
        maxNrOfRetries: 3,
        withinTimeRange: TimeSpan.FromMinutes(1),
        localOnlyDecider: exception => exception switch
        {
            InvalidReadingException => Directive.Restart,
            _ => Directive.Escalate,
        });

    private IActorRef ChildFor(string station)
    {
        // Actor names have to be URL-safe and stable, and a station id arrives from a route
        // parameter, so it is escaped rather than trusted.
        var name = $"station-{Uri.EscapeDataString(station)}";
        var child = Context.Child(name);

        return child.Equals(ActorRefs.Nobody)
            ? Context.ActorOf(
                WeatherStationActor.CreateProps(station, _hub, _loggerFactory.CreateLogger<WeatherStationActor>()),
                name)
            : child;
    }
}
