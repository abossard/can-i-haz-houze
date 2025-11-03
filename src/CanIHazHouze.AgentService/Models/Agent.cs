using System.ComponentModel.DataAnnotations;

namespace CanIHazHouze.AgentService.Models;

public class Agent
{
    [Required]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    // Partition key - same as Id for agent entities
    [Required]
    public string AgentId { get; set; } = string.Empty;
    
    // Entity type discriminator for mixed collection
    public string EntityType { get; set; } = "agent";
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    [Required]
    public string Prompt { get; set; } = string.Empty;
    
    public AgentConfig Config { get; set; } = new();
    
    // Available tools: LedgerAPI, CRMAPI, DocumentsAPI, AgentWorkbench, WebSearch
    public List<string> Tools { get; set; } = new();
    
    public List<AgentInputVariable> InputVariables { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AgentConfig
{
    // Deployment name (must match one of the configured deployments in AppHost)
    // Available options: gpt-5, gpt-5-mini, gpt-5-nano, gpt-41, gpt-41-mini, gpt-41-nano
    public string Model { get; set; } = "gpt-41-mini";
    
    public double Temperature { get; set; } = 0.7;
    
    public double TopP { get; set; } = 1.0;
    
    public int MaxTokens { get; set; } = 2000;
    
    public double FrequencyPenalty { get; set; } = 0.0;
    
    public double PresencePenalty { get; set; } = 0.0;
    
    public int MaxTurns { get; set; } = 10;
    
    public bool EnableMultiTurn { get; set; } = true;
    
    public string? GoalCompletionPrompt { get; set; }
    
    // Enable web search capabilities (requires WebSearch tool)
    public bool EnableWebSearch { get; set; } = false;
    
    // API key for web search (e.g., Google Custom Search API key or Bing Search API key)
    public string WebSearchApiKey { get; set; } = string.Empty;
    
    // Search engine ID for Google Custom Search (if using Google)
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
    public string Name { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public bool Required { get; set; } = true;
}
