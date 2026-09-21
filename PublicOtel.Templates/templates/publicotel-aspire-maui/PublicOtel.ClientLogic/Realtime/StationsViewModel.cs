using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// Drives the stations page. Free of MAUI types, like <see cref="WeatherViewModel"/>, so it
/// can be exercised directly from tests.
/// </summary>
public partial class StationsViewModel : ObservableObject
{
	private readonly IStationApiClient _stationApi;
	private readonly IStationHubClient _hub;

	/// <summary>
	/// Captured at construction, when the container resolves this on the UI thread.
	/// </summary>
	/// <remarks>
	/// SignalR raises its callbacks on a background thread, and mutating an
	/// ObservableCollection off the UI thread is what turns a live update into a crash in the
	/// CollectionView. Posting to the captured context keeps this project free of MAUI types.
	/// Under test there is no context, so the update simply runs inline.
	/// </remarks>
	private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

	public StationsViewModel(IStationApiClient stationApi, IStationHubClient hub)
	{
		_stationApi = stationApi;
		_hub = hub;

		_hub.ReadingReported += OnReadingReported;
		_hub.Reconnected += OnReconnected;
	}

	[ObservableProperty]
	public partial string Station { get; set; } = "north";

	[ObservableProperty]
	public partial int TemperatureC { get; set; } = 20;

	[ObservableProperty]
	public partial StationReading? LatestReading { get; set; }

	[ObservableProperty]
	public partial string Status { get; set; } = "Not connected.";

	/// <summary>Newest first, so the most recent push is at the top of the screen.</summary>
	public ObservableCollection<StationReading> Updates { get; } = [];

	[RelayCommand]
	private async Task ConnectAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _hub.StartAsync(cancellationToken);
			LatestReading = await _stationApi.GetLatestReadingAsync(Station, cancellationToken);
			Status = "Live. Report a reading from another device and watch this update.";
		}
		catch (Exception ex)
		{
			Status = $"Could not connect: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task ReportReadingAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _stationApi.ReportReadingAsync(Station, TemperatureC, cancellationToken);
			Status = $"Reported {TemperatureC}°C for {Station}.";
		}
		catch (Exception ex)
		{
			Status = $"Report failed: {ex.Message}";
		}
	}

	private void OnReadingReported(StationReading reading) => OnUiThread(() =>
	{
		LatestReading = reading;
		Updates.Insert(0, reading);
	});

	/// <summary>
	/// Re-fetch after a reconnect. Anything broadcast while the connection was down is simply
	/// gone, so resuming the stream without re-fetching leaves the UI quietly stale.
	/// </summary>
	private async void OnReconnected()
	{
		try
		{
			var latest = await _stationApi.GetLatestReadingAsync(Station);
			OnUiThread(() =>
			{
				LatestReading = latest;
				Status = "Reconnected.";
			});
		}
		catch (Exception ex)
		{
			OnUiThread(() => Status = $"Reconnected, but could not refresh: {ex.Message}");
		}
	}

	private void OnUiThread(Action action)
	{
		if (_uiContext is null)
		{
			action();
		}
		else
		{
			_uiContext.Post(_ => action(), null);
		}
	}
}
