using Microsoft.Extensions.Configuration;

namespace PublicOtel.ClientLogic.Realtime;

/// <summary>
/// Finds the API service's base address in the service discovery keys Aspire injects as
/// environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Typed HttpClients never need this: AddServiceDiscovery() rewrites "https+http://apiservice"
/// at send time. SignalR's HubConnection is built directly and never touches
/// HttpClientFactory, so the address has to be resolved by hand exactly once.
/// </para>
/// <para>
/// The keys are <c>services__apiservice__https__0</c> and <c>services__apiservice__http__0</c>.
/// PublicOtel.MauiServiceDefaults calls <c>Configuration.AddEnvironmentVariables()</c>
/// specifically so they are visible here - MauiApp.CreateBuilder() starts with empty
/// configuration, unlike the ASP.NET Core host.
/// </para>
/// </remarks>
public static class ApiServiceAddress
{
	public static Uri Resolve(IConfiguration configuration, string serviceName = "apiservice")
	{
		var address = configuration[$"services:{serviceName}:https:0"]
			?? configuration[$"services:{serviceName}:http:0"]
			?? throw new InvalidOperationException(
				$"No service discovery entry for '{serviceName}'. This app has to be started by "
				+ "the Aspire AppHost, which is what injects the address.");

		return new Uri(address, UriKind.Absolute);
	}
}
