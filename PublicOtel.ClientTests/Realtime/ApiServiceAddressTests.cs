using Microsoft.Extensions.Configuration;
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.ClientTests.Realtime;

/// <summary>
/// HttpClient gets Aspire service discovery for free through AddServiceDiscovery(). SignalR's
/// HubConnection does not go through HttpClientFactory, so it needs the address looked up
/// explicitly - which is what this does, from the same keys Aspire injects.
/// </summary>
public class ApiServiceAddressTests
{
	private static IConfiguration Config(params (string Key, string Value)[] entries) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
			.Build();

	[Fact]
	public void Resolve_prefers_the_https_endpoint()
	{
		var configuration = Config(
			("services:apiservice:https:0", "https://localhost:7301"),
			("services:apiservice:http:0", "http://localhost:5301"));

		ApiServiceAddress.Resolve(configuration).ShouldBe(new Uri("https://localhost:7301"));
	}

	[Fact]
	public void Resolve_falls_back_to_http_when_there_is_no_https_endpoint()
	{
		var configuration = Config(("services:apiservice:http:0", "http://localhost:5301"));

		ApiServiceAddress.Resolve(configuration).ShouldBe(new Uri("http://localhost:5301"));
	}

	[Fact]
	public void Resolve_throws_a_useful_message_when_the_app_was_not_launched_by_the_AppHost()
	{
		var exception = Should.Throw<InvalidOperationException>(() => ApiServiceAddress.Resolve(Config()));

		exception.Message.ShouldContain("apiservice");
		exception.Message.ShouldContain("AppHost");
	}
}
