using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json; // Cosmos SDK uses Newtonsoft serialization

namespace CanIHazHouze.AgentService.Models;

public class Agent
{
    // Cosmos DB requires a lowercase 'id' property – attribute ensures correct JSON field name
    [Required]
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    // Partition key - same as Id for agent entities
    [Required]
    [JsonProperty(PropertyName = "agentId")]
    public string AgentId { get; set; } = string.Empty;
    
    // Entity type discriminator for mixed collection
    [JsonProperty(PropertyName = "entityType")]
    public string EntityType { get; set; } = "agent";
    
    [Required]
    [JsonProperty(PropertyName = "name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "description")]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    [JsonProperty(PropertyName = "prompt")]
    public string Prompt { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "config")]
    public AgentConfig Config { get; set; } = new();
    
    // Available tools: LedgerAPI, CRMAPI, DocumentsAPI, AgentWorkbench, WebSearch
    [JsonProperty(PropertyName = "tools")]
    public List<string> Tools { get; set; } = new();
    
    [JsonProperty(PropertyName = "inputVariables")]
    public List<AgentInputVariable> InputVariables { get; set; } = new();
    
    [JsonProperty(PropertyName = "createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonProperty(PropertyName = "updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AgentConfig
{
    // Deployment name (must match one of the configured deployments in AppHost)
    // Available options are exposed by AvailableModels.All
    [JsonProperty(PropertyName = "model")]
    public string Model { get; set; } = "gpt-41-mini";
    
    [JsonProperty(PropertyName = "temperature")]
    public double Temperature { get; set; } = 0.7;
    
    [JsonProperty(PropertyName = "topP")]
    public double TopP { get; set; } = 1.0;
    
    [JsonProperty(PropertyName = "maxTokens")]
    public int MaxTokens { get; set; } = 2000;
    
    [JsonProperty(PropertyName = "frequencyPenalty")]
    public double FrequencyPenalty { get; set; } = 0.0;
    
    [JsonProperty(PropertyName = "presencePenalty")]
    public double PresencePenalty { get; set; } = 0.0;
    
    [JsonProperty(PropertyName = "maxTurns")]
    public int MaxTurns { get; set; } = 10;
    
    [JsonProperty(PropertyName = "enableMultiTurn")]
    public bool EnableMultiTurn { get; set; } = true;
    
    [JsonProperty(PropertyName = "goalCompletionPrompt")]
    public string? GoalCompletionPrompt { get; set; }
    
    // Enable web search capabilities (requires WebSearch tool)
    [JsonProperty(PropertyName = "enableWebSearch")]
    public bool EnableWebSearch { get; set; } = false;
    
    // API key for web search (e.g., Google Custom Search API key or Bing Search API key)
    [JsonProperty(PropertyName = "webSearchApiKey")]
    public string WebSearchApiKey { get; set; } = string.Empty;
    
    // Search engine ID for Google Custom Search (if using Google)
    [JsonProperty(PropertyName = "webSearchEngineId")]
    public string WebSearchEngineId { get; set; } = string.Empty;
}

public static class AvailableModels
{
    public static readonly List<ModelDeployment> All = new()
    {
        new ModelDeployment 
        { 
            DeploymentName = "gpt-4o", 
            DisplayName = "gpt-4o", 
            Description = "Flagship reasoning model for logic-heavy tasks, deep analytics, and code generation" 
        },
        new ModelDeployment 
        { 
            DeploymentName = "gpt-4o-mini", 
            DisplayName = "gpt-4o Mini", 
            Description = "Lightweight gpt-4o for cost-sensitive use cases with reasoning capabilities" 
        },
        new ModelDeployment
        {
            DeploymentName = "gpt-5-nano",
            DisplayName = "gpt-5 Nano",
            Description = "Fastest low-latency model for lightweight tasks and quick agent responses"
        },
    };
}

public class ModelDeployment
{
    public string DeploymentName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AgentInputVariable
{
    [Required]
    [JsonProperty(PropertyName = "name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "required")]
    public bool Required { get; set; } = true;
}
