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
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"OpenAI connection string '{connectionName}' not found in configuration.");
        }

        // Parse Aspire connection string format: Endpoint=https://...;Key=...
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var endpoint = parts.FirstOrDefault(p => p.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        var key = parts.FirstOrDefault(p => p.StartsWith("Key=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException($"Invalid OpenAI connection string format. Expected: Endpoint=<url>;Key=<key>");
        }

        // Register OpenAI configuration for Semantic Kernel dynamic kernel creation
        builder.Services.Configure<OpenAIConfiguration>(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = key;
        });

        // Also register AzureOpenAIClient for direct Azure SDK access if needed
        builder.Services.AddSingleton(sp =>
        {
            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        });
    }
}
