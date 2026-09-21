namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Thrown for a physically implausible reading. It exists so that
/// <see cref="StationSupervisor"/> has a real failure to make a decision about, rather than a
/// contrived one.
/// </summary>
public sealed class InvalidReadingException(string station, int temperatureC)
    : Exception($"Station '{station}' reported {temperatureC}°C, which is outside the supported range.")
{
    public string Station { get; } = station;

    public int TemperatureC { get; } = temperatureC;
}
