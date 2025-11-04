using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CanIHazHouze.AgentService.Configuration;
using Xunit;

namespace CanIHazHouze.Tests;

/// <summary>
/// End-to-end API test exercising AgentService CRUD and run execution path.
/// Validates: create -> get -> list -> run -> poll status.
/// 
/// REQUIRES: Azure OpenAI endpoint configured via user secrets or environment.
/// Run: dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://YOUR-RESOURCE.openai.azure.com/" --project CanIHazHouze.Tests.csproj
/// </summary>
public class AgentServiceEndToEndTests
{
    private static WebApplicationFactory<CanIHazHouze.AgentService.Program> CreateFactory()
    {
        // Test uses real Azure OpenAI configuration from user secrets or environment variables
        // No dummy/stub configuration - this ensures tests validate actual Azure integration
        var factory = new WebApplicationFactory<CanIHazHouze.AgentService.Program>();
        return factory;
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task Agent_E2E_CreateListGetRun()
    {
        // Arrange
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var agentName = $"e2e-agent-{Guid.NewGuid():N}";

        var createPayload = new
        {
            Name = agentName,
            Description = "E2E test agent",
            Prompt = "Respond with the word 'pong'.",
            Tools = Array.Empty<string>(),
            Config = new
            {
                Model = "gpt-4o-mini",
                Temperature = 0.1,
                TopP = 1.0,
                MaxTokens = 64,
                FrequencyPenalty = 0.0,
                PresencePenalty = 0.0,
                MaxTurns = 1,
                EnableMultiTurn = false
            },
            InputVariables = Array.Empty<object>()
        };

        // Act: Create agent
        var ct = TestContext.Current.CancellationToken;
        var createResponse = await client.PostAsJsonAsync("/agents", createPayload, ct);
        var createBody = await createResponse.Content.ReadAsStringAsync(ct);
        Assert.True(createResponse.IsSuccessStatusCode, $"Create failed: {createResponse.StatusCode} {createBody}");

        using var doc = JsonDocument.Parse(createBody);
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id), "Returned agent id was null/empty");

        // Act: Get agent by id
        var getResponse = await client.GetAsync($"/agents/{id}", ct);
        var getBody = await getResponse.Content.ReadAsStringAsync(ct);
        Assert.True(getResponse.IsSuccessStatusCode, $"Get failed: {getResponse.StatusCode} {getBody}");
        using var getDoc = JsonDocument.Parse(getBody);
        Assert.Equal(agentName, getDoc.RootElement.GetProperty("name").GetString());

        // Act: List agents and ensure presence
        var listResponse = await client.GetAsync("/agents", ct);
        var listBody = await listResponse.Content.ReadAsStringAsync(ct);
        Assert.True(listResponse.IsSuccessStatusCode, $"List failed: {listResponse.StatusCode} {listBody}");
        using var listDoc = JsonDocument.Parse(listBody);
        var listIds = listDoc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains(id, listIds);

        // Act: Run agent (synchronous path)
        var runResponse = await client.PostAsJsonAsync($"/agents/{id}/run", new { }, ct);
        var runBody = await runResponse.Content.ReadAsStringAsync(ct);
        Assert.True(runResponse.IsSuccessStatusCode, $"Run failed: {runResponse.StatusCode} {runBody}");
        using var runDoc = JsonDocument.Parse(runBody);
        var runId = runDoc.RootElement.GetProperty("id").GetString();
        var status = runDoc.RootElement.GetProperty("status").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.False(string.IsNullOrWhiteSpace(status));

        // Poll run status if still running (limited retries)
        if (status == "running")
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(1000, ct);
                var poll = await client.GetAsync($"/runs/{id}/{runId}", ct);
                var pollBody = await poll.Content.ReadAsStringAsync(ct);
                if (!poll.IsSuccessStatusCode)
                {
                    throw new Xunit.Sdk.XunitException($"Polling run failed: {poll.StatusCode} {pollBody}");
                }
                using var pollDoc = JsonDocument.Parse(pollBody);
                status = pollDoc.RootElement.GetProperty("status").GetString();
                if (status != "running") break;
            }
        }

        // Assert final status
        Assert.Contains(status, new[] { "completed", "failed" });
        if (status == "failed")
        {
            var error = runDoc.RootElement.GetProperty("error").GetString();
            Assert.False(string.IsNullOrWhiteSpace(error), "Failed status without error message");
        }
    }
}
