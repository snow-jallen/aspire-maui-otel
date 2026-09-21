using System.Security.Cryptography;
using System.Text;
using Akka.Actor;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Parent of one <see cref="WeatherStationActor"/> per station id - the actor-per-entity
/// pattern - and the place where failure decisions are made.
/// </summary>
public sealed class StationSupervisor : ReceiveActor
{
    private readonly IHubContext<WeatherHub, IWeatherClient> _hub;

    public StationSupervisor(IHubContext<WeatherHub, IWeatherClient> hub)
    {
        _hub = hub;

        // Forward, not Tell: Forward preserves the original Sender, so an Ask that arrives
        // here gets its reply straight from the child. Tell would make the supervisor the
        // sender and the caller's Ask would time out.
        Receive<ReportReading>(message => ChildFor(message.Station).Forward(message));
        Receive<GetLatestReading>(message => ChildFor(message.Station).Forward(message));
    }

    public static Props CreateProps(IHubContext<WeatherHub, IWeatherClient> hub) =>
        Props.Create(() => new StationSupervisor(hub));

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
        var name = ActorNameFor(station);
        var child = Context.Child(name);

        return child.Equals(ActorRefs.Nobody)
            ? Context.ActorOf(WeatherStationActor.CreateProps(station, _hub), name)
            : child;
    }

    /// <summary>
    /// A child actor name that Akka will always accept, for a station id that arrived from a
    /// route parameter and could contain anything.
    /// </summary>
    /// <remarks>
    /// Not <c>Uri.EscapeDataString</c>: that emits <c>%XX</c>, and <c>%</c> is not a legal
    /// actor-name character, so escaping a station id that way produces a name Akka itself
    /// rejects. The throw would land in this supervisor rather than in a child, where
    /// supervision could do nothing about it.
    /// </remarks>
    private static string ActorNameFor(string station)
    {
        var safe = new string(station.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        // Two different ids can flatten to the same safe string ("north side" and
        // "north-side"), and sharing one actor would silently merge two stations' readings.
        // The hash of the original keeps them apart, and is stable for a given id.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(station)))[..8];

        return $"station-{safe}-{hash}";
    }
}
