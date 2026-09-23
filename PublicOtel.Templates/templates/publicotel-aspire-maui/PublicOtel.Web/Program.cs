//#if (IncludeAkka)
using PublicOtel.ClientLogic.Realtime;
//#endif
using PublicOtel.Web;
using PublicOtel.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

//#if (IncludeAkka)
// The Stations page: the same client code the MAUI app uses, registered per circuit instead
// of per process. In Blazor Server a scoped service lives as long as one browser tab's
// connection, so each tab gets its own view model and its own SignalR connection to the API -
// and the view model survives navigating away from the page and back.
builder.Services.AddHttpClient<IStationApiClient, StationApiClient>(client =>
    {
        client.BaseAddress = new("https+http://apiservice");
    });

// HubConnection does not go through HttpClientFactory, so service discovery never rewrites
// its address; it is resolved by hand from the keys the AppHost injects. Deferred, so a
// missing key fails the Connect button rather than the whole site.
builder.Services.AddScoped<IStationHubClient>(_ =>
    new StationHubClient(() =>
        new Uri(ApiServiceAddress.Resolve(builder.Configuration), "/hubs/weather")));

builder.Services.AddScoped<StationsViewModel>();
//#endif

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
