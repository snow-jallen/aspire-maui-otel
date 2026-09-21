using Akka.Actor;
using Akka.Hosting;

namespace PublicOtel.ApiService.Actors;

/// <summary>The body of a reading report.</summary>
public sealed record ReadingRequest(int TemperatureC);

public static class WeatherStationEndpoints
{
    /// <summary>
    /// How long the Ask endpoint waits for an actor to reply. There is no such thing as an
    /// Ask without a timeout; declining to pick one just means accepting a default.
    /// </summary>
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapWeatherStationEndpoints(this IEndpointRouteBuilder app)
    {
        // Tell: fire and forget. 202 Accepted is the honest status code - the actor has not
        // necessarily processed this yet, and saying 200 OK would be a small lie about what
        // asynchronous messaging is.
        app.MapPost("/stations/{station}/readings",
            (string station, ReadingRequest request, IRequiredActor<StationSupervisor> stations) =>
            {
                stations.ActorRef.Tell(
                    new ReportReading(station, request.TemperatureC, TraceEnvelope.Current()));

                return Results.Accepted($"/stations/{station}");
            })
            .WithName("ReportReading");

        // Ask: request/response faked on top of asynchronous messaging. What it costs, in
        // full: a timeout you have to choose, a temporary actor and a continuation held open
        // while you wait, and an AskTimeoutException failure mode that Tell simply does not
        // have. Worth it only when the HTTP response body needs the answer - as it does here.
        app.MapGet("/stations/{station}",
            async (string station, IRequiredActor<StationSupervisor> stations) =>
            {
                var reply = await stations.ActorRef.Ask(
                    new GetLatestReading(station, TraceEnvelope.Current()),
                    AskTimeout);

                return reply switch
                {
                    StationReading reading => Results.Ok(reading),
                    _ => Results.NotFound(),
                };
            })
            .WithName("GetLatestReading");

        return app;
    }
}
