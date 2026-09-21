using Microsoft.AspNetCore.SignalR;
using PublicOtel.ApiService.Actors;

namespace PublicOtel.ApiService.Hubs;

/// <summary>
/// The methods the server can call on a connected client. Declaring them on an interface and
/// using <see cref="Hub{T}"/> means a typo in a client method name is a compile error instead
/// of a message that silently goes nowhere.
/// </summary>
public interface IWeatherClient
{
    Task ReadingReported(StationReading reading);
}

/// <summary>
/// The hub at <c>/hubs/weather</c>. It deliberately has no methods of its own: clients only
/// listen here, and report readings over HTTP instead.
/// </summary>
/// <remarks>
/// That split is on purpose. An HTTP POST carries a <c>traceparent</c> header and is picked up
/// by ASP.NET Core auto-instrumentation, so the report is already inside a trace by the time
/// it reaches the endpoint. A hub method invocation is not, and would start the story with a
/// broken trace.
/// </remarks>
public sealed class WeatherHub : Hub<IWeatherClient>;
