using CanIHazHouze.Web;
using CanIHazHouze.Web.Components;
using CanIHazHouze.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Microsoft.AspNetCore.Http.Connections;

var builder = WebApplication.CreateBuilder(args);

// Set default culture to ensure proper currency formatting
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Configure response compression for better performance
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
    opts.EnableForHttps = true;
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// Add toast notification service
builder.Services.AddScoped<ToastService>();

// Add background activity tracking service
builder.Services.AddScoped<BackgroundActivityService>();

// Add service URL resolver for client-side connections
builder.Services.AddSingleton<IServiceUrlResolver, ServiceUrlResolver>();

// Add error handling delegating handler
builder.Services.AddTransient<ErrorHandlingDelegatingHandler>();

// Configure circuit options for production scalability (100+ concurrent users)
builder.Services.AddServerSideBlazor(options =>
{
    // Enable detailed errors only in development
    options.DetailedErrors = builder.Environment.IsDevelopment();
    
    // Circuit retention settings - optimized for 100+ concurrent users
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    
    // Timeout settings for reliability through corporate proxies
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
        // Increased timeout for agent execution (agents can take longer to complete)
        client.Timeout = TimeSpan.FromMinutes(2);
    })
    .AddHttpMessageHandler<ErrorHandlingDelegatingHandler>();

var app = builder.Build();

// Enable response compression for production performance
app.UseResponseCompression();

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

// Configure Blazor Hub to use only Long Polling (no WebSockets)
// This ensures maximum compatibility through corporate proxies
app.MapBlazorHub(options =>
{
    options.Transports = HttpTransportType.LongPolling;
});

app.MapDefaultEndpoints(enableMcp: false);

app.Run();
