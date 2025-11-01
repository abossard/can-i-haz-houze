using System.Text.Json.Serialization;

namespace CanIHazHouze.Web;

public class AgentApiClient(HttpClient httpClient)
{
    public async Task<List<Agent>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<Agent>>("/agents", cancellationToken)
            ?? new List<Agent>();
    }

    public async Task<Agent?> GetAgentAsync(string id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<Agent>($"/agents/{id}", cancellationToken);
    }

    public async Task<Agent?> CreateAgentAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/agents", agent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Agent>(cancellationToken);
    }

    public async Task<Agent?> UpdateAgentAsync(string id, Agent agent, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/agents/{id}", agent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Agent>(cancellationToken);
    }

    public async Task DeleteAgentAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/agents/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AgentRun?> RunAgentAsync(string agentId, Dictionary<string, string> inputValues, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/agents/{agentId}/run", inputValues, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentRun>(cancellationToken);
    }

    public async Task<AgentRun?> GetRunAsync(string agentId, string id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<AgentRun>($"/runs/{agentId}/{id}", cancellationToken);
    }

    public async Task<List<AgentRun>> GetAgentRunsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<AgentRun>>($"/agents/{agentId}/runs", cancellationToken)
            ?? new List<AgentRun>();
    }

    public async Task<string?> RunAgentAsyncAsync(string agentId, Dictionary<string, string> inputValues, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/agents/{agentId}/run-async", inputValues, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AsyncRunResponse>(cancellationToken);
        return result?.RunId;
    }

    public async Task PauseRunAsync(string agentId, string runId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/runs/{agentId}/{runId}/pause", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResumeRunAsync(string agentId, string runId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/runs/{agentId}/{runId}/resume", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task CancelRunAsync(string agentId, string runId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/runs/{agentId}/{runId}/cancel", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ActiveRunsResponse?> GetActiveRunsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ActiveRunsResponse>("/runs/active", cancellationToken);
    }
}

public class AsyncRunResponse
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class ActiveRunsResponse
{
    [JsonPropertyName("activeRuns")]
    public List<string> ActiveRuns { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class Agent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

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

    [JsonPropertyName("maxTurns")]
    public int MaxTurns { get; set; } = 10;

    [JsonPropertyName("enableMultiTurn")]
    public bool EnableMultiTurn { get; set; } = true;

    [JsonPropertyName("goalCompletionPrompt")]
    public string? GoalCompletionPrompt { get; set; }
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

    [JsonPropertyName("conversationHistory")]
    public List<ConversationTurn> ConversationHistory { get; set; } = new();

    [JsonPropertyName("turnCount")]
    public int TurnCount { get; set; } = 0;

    [JsonPropertyName("maxTurns")]
    public int MaxTurns { get; set; } = 10;

    [JsonPropertyName("goal")]
    public string? Goal { get; set; }

    [JsonPropertyName("goalAchieved")]
    public bool GoalAchieved { get; set; } = false;

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("pausedAt")]
    public DateTime? PausedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ConversationTurn
{
    [JsonPropertyName("turnNumber")]
    public int TurnNumber { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("toolCalls")]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string? Result { get; set; }
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
