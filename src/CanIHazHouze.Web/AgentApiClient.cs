using System.Text.Json.Serialization;

namespace CanIHazHouze.Web;

public class AgentApiClient(HttpClient httpClient)
{
    public async Task<List<Agent>> GetAgentsAsync(string owner, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<Agent>>($"/agents/{owner}", cancellationToken)
            ?? new List<Agent>();
    }

    public async Task<Agent?> GetAgentAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<Agent>($"/agents/{owner}/{id}", cancellationToken);
    }

    public async Task<Agent?> CreateAgentAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/agents", agent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Agent>(cancellationToken);
    }

    public async Task<Agent?> UpdateAgentAsync(string owner, string id, Agent agent, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/agents/{owner}/{id}", agent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Agent>(cancellationToken);
    }

    public async Task DeleteAgentAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/agents/{owner}/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AgentRun?> RunAgentAsync(string owner, string agentId, Dictionary<string, string> inputValues, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/agents/{owner}/{agentId}/run", inputValues, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentRun>(cancellationToken);
    }

    public async Task<AgentRun?> GetRunAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<AgentRun>($"/runs/{owner}/{id}", cancellationToken);
    }

    public async Task<List<AgentRun>> GetAgentRunsAsync(string owner, string agentId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<AgentRun>>($"/agents/{owner}/{agentId}/runs", cancellationToken)
            ?? new List<AgentRun>();
    }
}

public class Agent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public AgentConfig Config { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = new();

    [JsonPropertyName("inputVariables")]
    public List<AgentInputVariable> InputVariables { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class AgentConfig
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o-mini";

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("topP")]
    public double TopP { get; set; } = 1.0;

    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 2000;

    [JsonPropertyName("frequencyPenalty")]
    public double FrequencyPenalty { get; set; } = 0.0;

    [JsonPropertyName("presencePenalty")]
    public double PresencePenalty { get; set; } = 0.0;
}

public class AgentInputVariable
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}

public class AgentRun
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("inputValues")]
    public Dictionary<string, string> InputValues { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("logs")]
    public List<AgentRunLog> Logs { get; set; } = new();

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

public class AgentRunLog
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}
