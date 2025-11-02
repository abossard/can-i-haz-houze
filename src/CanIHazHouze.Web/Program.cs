using CanIHazHouze.Web;
using CanIHazHouze.Web.Components;
using CanIHazHouze.Web.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Set default culture to ensure proper currency formatting
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// Add toast notification service
builder.Services.AddScoped<ToastService>();

// Add background activity tracking service
builder.Services.AddScoped<BackgroundActivityService>();

// Add error handling delegating handler
builder.Services.AddTransient<ErrorHandlingDelegatingHandler>();

// Configure circuit options for better performance and reconnection
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});

builder.Services.AddHttpClient<DocumentApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://documentservice");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<ErrorHandlingDelegatingHandler>();

builder.Services.AddHttpClient<LedgerApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://ledgerservice");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<ErrorHandlingDelegatingHandler>();

builder.Services.AddHttpClient<MortgageApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://mortgageapprover");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<ErrorHandlingDelegatingHandler>();

builder.Services.AddHttpClient<CrmApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://crmservice");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<ErrorHandlingDelegatingHandler>();

builder.Services.AddHttpClient<AgentApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://agentservice");
    });

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
