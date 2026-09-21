using Microsoft.AspNetCore.SignalR.Client;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// The client half of the SignalR connection. Kept deliberately free of logic so that
/// everything worth testing lives in <see cref="StationsViewModel"/> behind this interface.
/// </summary>
public interface IStationHubClient : IAsyncDisposable
{
	/// <summary>Raised when the server broadcasts a reading. Not raised on the UI thread.</summary>
	event Action<StationReading>? ReadingReported;

	/// <summary>
	/// Raised after the connection comes back. Anything broadcast while it was down was
	/// missed, so the subscriber has to re-fetch state.
	/// </summary>
	event Action? Reconnected;

	Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class StationHubClient(Func<Uri> hubUriFactory) : IStationHubClient
{
	/// <summary>
	/// Must match <c>IWeatherClient.ReadingReported</c> on the server. SignalR matches client
	/// methods by name at runtime, so a typo here is a silent no-op rather than an error.
	/// </summary>
	private const string ReadingReportedMethod = "ReadingReported";

	private HubConnection? _connection;

	public event Action<StationReading>? ReadingReported;

	public event Action? Reconnected;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		// Built here rather than in the constructor. Resolving the API's address throws when
		// the app was not launched by the Aspire AppHost, and this type is resolved eagerly at
		// startup, so a constructor that can throw would take the whole app down - including
		// the weather page, which needs no SignalR at all. Deferring it puts the failure inside
		// StartAsync, where the view model already catches it and shows it.
		_connection ??= Build(hubUriFactory());

		await _connection.StartAsync(cancellationToken);
	}

	private HubConnection Build(Uri hubUri)
	{
		var connection = new HubConnectionBuilder()
			.WithUrl(hubUri)
			.WithAutomaticReconnect()
			.Build();

		connection.On<StationReading>(ReadingReportedMethod, reading => ReadingReported?.Invoke(reading));

		connection.Reconnected += _ =>
		{
			Reconnected?.Invoke();
			return Task.CompletedTask;
		};

		return connection;
	}

	public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}
