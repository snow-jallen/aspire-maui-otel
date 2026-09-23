using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Puts the actor system in the DI container and starts the one top-level actor.
/// </summary>
public static class WeatherActorExtensions
{
    // Lower-case with separators removed, on purpose. In the template this literal is a token
    // the actorSystemName symbol rewrites per generated app. It cannot be the plain
    // lower-cased project name: Akka accepts only [a-zA-Z0-9] and a non-leading '-', so a
    // dotted name like Contoso.Telemetry would throw at ActorSystem creation.
    private const string ActorSystemName = "publicotel";

    public static IHostApplicationBuilder AddWeatherActors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAkka(ActorSystemName, (akka, serviceProvider) =>
        {
            // Without this, Akka writes its own log output - actor start-up, supervision
            // restarts, dead letters - straight to the console in its own format, and none
            // of it reaches OpenTelemetry. AddLoggerFactory routes it through the app's
            // ILoggerFactory instead, so it lands in the dashboard's structured logs with
            // everything else. It does not attach those records to a trace; see the remarks
            // on WeatherStationActor for why, and for the logger that does.
            akka.ConfigureLoggers(loggers =>
            {
                loggers.ClearLoggers();
                loggers.AddLoggerFactory();
            });

            akka.WithActors((system, registry) =>
            {
                // The hub context is resolved once, here, and handed to the actors. Actors
                // must not reach into the container themselves: an actor's dependencies
                // belong in its Props so that a test can supply different ones.
                var hub = serviceProvider.GetRequiredService<IHubContext<WeatherHub, IWeatherClient>>();

                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                var supervisor = system.ActorOf(StationSupervisor.CreateProps(hub, loggerFactory), "stations");

                // Registering by type is what lets an endpoint take IRequiredActor<StationSupervisor>.
                registry.Register<StationSupervisor>(supervisor);
            });
        });

        return builder;
    }
}
