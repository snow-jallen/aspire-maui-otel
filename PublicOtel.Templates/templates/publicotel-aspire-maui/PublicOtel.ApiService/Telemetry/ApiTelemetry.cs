using System.Diagnostics;

namespace PublicOtel.ApiService.Telemetry;

/// <summary>
/// The API service's own <see cref="ActivitySource"/>.
/// </summary>
/// <remarks>
/// The name is deliberately the assembly name, which is also
/// <c>builder.Environment.ApplicationName</c>. PublicOtel.ServiceDefaults already calls
/// <c>tracing.AddSource(builder.Environment.ApplicationName)</c>, so spans started here are
/// exported with no further registration. The coupling is invisible from either file, which
/// is why it is written down here.
/// </remarks>
public static class ApiTelemetry
{
    public const string ActivitySourceName = "PublicOtel.ApiService";

    public static ActivitySource Source { get; } = new(ActivitySourceName);
}
