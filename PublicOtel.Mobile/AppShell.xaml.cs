using Microsoft.Extensions.DependencyInjection;

namespace PublicOtel.Mobile;

public partial class AppShell : Shell
{
	/// <summary>
	/// Pages are resolved from the container here rather than taken as constructor
	/// parameters. This is the composition root and its only job is assembling pages, so
	/// adding a tab stays a change in two obvious places - the XAML and one line here -
	/// instead of also rewriting this signature.
	/// </summary>
	public AppShell(IServiceProvider services)
	{
		InitializeComponent();

		HomeContent.Content = services.GetRequiredService<MainPage>();
		StationsContent.Content = services.GetRequiredService<StationsPage>();
	}
}
