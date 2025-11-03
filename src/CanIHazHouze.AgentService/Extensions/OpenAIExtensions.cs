using Azure;
using Azure.AI.OpenAI;
using CanIHazHouze.AgentService.Configuration;

namespace CanIHazHouze.AgentService.Extensions;

/// <summary>
/// Extension methods for configuring Azure OpenAI services with Aspire integration.
/// </summary>
public static class OpenAIExtensions
{
    /// <summary>
    /// Adds Azure OpenAI configuration from Aspire connection strings.
    /// This method properly integrates with Aspire's service discovery and configuration patterns.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="connectionName">The name of the OpenAI connection (default: "openai").</param>
    public static void AddAzureOpenAIConfiguration(this WebApplicationBuilder builder, string connectionName = "openai")
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        // Fallback to environment-style variables if connection string not present
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var envEndpoint = builder.Configuration["OPENAI_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OPENAI_ENDPOINT");
            var envKey = builder.Configuration["OPENAI_API_KEY"]
                        ?? builder.Configuration["OPENAI_KEY"]
                        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                        ?? Environment.GetEnvironmentVariable("OPENAI_KEY");

            if (!string.IsNullOrWhiteSpace(envEndpoint) && !string.IsNullOrWhiteSpace(envKey))
            {
                Register(builder, envEndpoint, envKey, source: "environment variables");
                return;
            }

            throw new InvalidOperationException($"OpenAI connection string '{connectionName}' not found. Set user-secret 'ConnectionStrings:{connectionName}' to 'Endpoint=<url>;Key=<key>' or provide OPENAI_ENDPOINT + OPENAI_API_KEY environment variables.");
        }

        // Parse flexible connection string: supports Endpoint= / Url= and Key=/ApiKey=/Api-Key=
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var kvPairs = parts
            .Select(p => p.Split('=', 2))
            .Where(a => a.Length == 2)
            .ToDictionary(a => a[0].Trim(), a => a[1].Trim(), StringComparer.OrdinalIgnoreCase);

        kvPairs.TryGetValue("Endpoint", out var endpoint);
        if (string.IsNullOrWhiteSpace(endpoint) && kvPairs.TryGetValue("Url", out var altEndpoint))
        {
            endpoint = altEndpoint;
        }

        string? key = null;
        if (!kvPairs.TryGetValue("Key", out key))
        {
            kvPairs.TryGetValue("ApiKey", out key);
        }
        if (string.IsNullOrWhiteSpace(key) && kvPairs.TryGetValue("Api-Key", out var dashKey))
        {
            key = dashKey;
        }

        // Additional fallback to environment if parts missing
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = builder.Configuration["OPENAI_ENDPOINT"] ?? Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            key = builder.Configuration["OPENAI_API_KEY"]
                   ?? builder.Configuration["OPENAI_KEY"]
                   ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                   ?? Environment.GetEnvironmentVariable("OPENAI_KEY")
                   ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            var presentKeys = string.Join(", ", kvPairs.Keys.OrderBy(k => k));
            throw new InvalidOperationException($"Invalid OpenAI connection string. Provided keys: [{presentKeys}]. Expected at minimum 'Endpoint' (or 'Url') and 'Key' (or 'ApiKey'). Actual string format: Endpoint=<url>;Key=<key>");
        }

        Register(builder, endpoint, key, source: "connection string");
    }

    private static void Register(WebApplicationBuilder builder, string endpoint, string apiKey, string source)
    {
        // Basic sanitization for logs (do not log full key)
        var sanitizedKey = apiKey.Length <= 8 ? "***" : apiKey[..4] + "***" + apiKey[^4..];
        // Using Console.WriteLine here because ILoggerFactory isn't available yet at this configuration stage.
        Console.WriteLine($"[OpenAI] Configuring Azure OpenAI from {source} at {endpoint} with key fragment {sanitizedKey}");

        builder.Services.Configure<OpenAIConfiguration>(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
        });

        builder.Services.AddSingleton(_ => new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey)));
    }
}
