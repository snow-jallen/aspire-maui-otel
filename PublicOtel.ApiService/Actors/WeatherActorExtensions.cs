using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Hubs;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Puts the actor system in the DI container and starts the one top-level actor.
/// </summary>
public static class WeatherActorExtensions
{
    // Lower-case on purpose: in the template this literal is rewritten per generated app by
    // the lowerCaseName symbol, so each app names its actor system after itself.
    private const string ActorSystemName = "publicotel";

    public static IHostApplicationBuilder AddWeatherActors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAkka(ActorSystemName, (akka, serviceProvider) =>
        {
            akka.WithActors((system, registry) =>
            {
                // The hub context is resolved once, here, and handed to the actors. Actors
                // must not reach into the container themselves: an actor's dependencies
                // belong in its Props so that a test can supply different ones.
                var hub = serviceProvider.GetRequiredService<IHubContext<WeatherHub, IWeatherClient>>();

                var supervisor = system.ActorOf(StationSupervisor.CreateProps(hub), "stations");

                // Registering by type is what lets an endpoint take IRequiredActor<StationSupervisor>.
                registry.Register<StationSupervisor>(supervisor);
            });
        });

        return builder;
    }
}
