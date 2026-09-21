using System.Net;
using System.Text;
using PublicOtel.ClientLogic.Realtime;

namespace PublicOtel.ClientTests.Realtime;

public class StationApiClientTests
{
	/// <summary>
	/// A stub handler is used rather than a substituted HttpClient because HttpClient is a
	/// concrete class - SendAsync is the only seam it has.
	/// </summary>
	private sealed class StubHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
	{
		public HttpRequestMessage? LastRequest { get; private set; }

		public string? LastBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = request;
			LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
		}
	}

	private static StationApiClient ClientFor(StubHandler handler) =>
		new(new HttpClient(handler) { BaseAddress = new Uri("https://apiservice") });

	[Fact]
	public async Task ReportReading_posts_the_temperature_to_the_station_route()
	{
		var handler = new StubHandler(HttpStatusCode.Accepted);

		await ClientFor(handler).ReportReadingAsync("north", 21);

		handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
		handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/stations/north/readings");
		handler.LastBody.ShouldContain("21");
	}

	[Fact]
	public async Task ReportReading_escapes_a_station_name_that_needs_it()
	{
		var handler = new StubHandler(HttpStatusCode.Accepted);

		await ClientFor(handler).ReportReadingAsync("north side", 21);

		handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/stations/north%20side/readings");
	}

	[Fact]
	public async Task GetLatestReading_returns_the_reading()
	{
		var handler = new StubHandler(
			HttpStatusCode.OK,
			"""{"station":"north","temperatureC":21,"observedAt":"2026-09-21T10:00:00+00:00"}""");

		var reading = await ClientFor(handler).GetLatestReadingAsync("north");

		reading.ShouldNotBeNull();
		reading.Station.ShouldBe("north");
		reading.TemperatureC.ShouldBe(21);
	}

	[Fact]
	public async Task GetLatestReading_returns_null_for_a_station_that_has_not_reported()
	{
		var handler = new StubHandler(HttpStatusCode.NotFound);

		(await ClientFor(handler).GetLatestReadingAsync("nowhere")).ShouldBeNull();
	}

	[Fact]
	public async Task GetLatestReading_throws_on_a_server_error()
	{
		var handler = new StubHandler(HttpStatusCode.InternalServerError);

		await Should.ThrowAsync<HttpRequestException>(
			() => ClientFor(handler).GetLatestReadingAsync("north"));
	}
}
