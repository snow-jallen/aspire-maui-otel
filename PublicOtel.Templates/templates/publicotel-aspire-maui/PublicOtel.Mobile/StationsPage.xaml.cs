using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.Mobile;

public partial class StationsPage : ContentPage
{
	public StationsPage(StationsViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = viewModel;
	}
}
