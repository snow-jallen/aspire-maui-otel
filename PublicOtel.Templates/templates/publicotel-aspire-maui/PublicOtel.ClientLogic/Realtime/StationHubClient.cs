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

	private readonly SemaphoreSlim _gate = new(1, 1);

	public event Action<StationReading>? ReadingReported;

	public event Action? Reconnected;

	/// <summary>
	/// Connects, or does nothing if already connected. Safe to call more than once.
	/// </summary>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		// The gate does two jobs: it makes the lazy build atomic, and it stops two callers
		// racing into HubConnection.StartAsync at once.
		await _gate.WaitAsync(cancellationToken);

		try
		{
			_connection ??= Build(hubUriFactory());

			// HubConnection.StartAsync throws unless the connection is Disconnected, and the
			// Connect button is tappable twice. An already-live connection is what the caller
			// wanted, so report success rather than a confusing failure.
			if (_connection.State == HubConnectionState.Disconnected)
			{
				await _connection.StartAsync(cancellationToken);
			}
		}
		finally
		{
			_gate.Release();
		}
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

	public async ValueTask DisposeAsync()
	{
		if (_connection is not null)
		{
			await _connection.DisposeAsync();
		}

		_gate.Dispose();
	}
}
