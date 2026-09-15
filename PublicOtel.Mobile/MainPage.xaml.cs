using System.ComponentModel;
using PublicOtel.ClientLogic;

namespace PublicOtel.Mobile;

public partial class MainPage : ContentPage
{
	private readonly WeatherViewModel _viewModel;

	public MainPage(WeatherViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;

		// Keep the screen reader in step with the status line the view model publishes.
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(WeatherViewModel.Status))
		{
			SemanticScreenReader.Announce(_viewModel.Status);
		}
	}
}
