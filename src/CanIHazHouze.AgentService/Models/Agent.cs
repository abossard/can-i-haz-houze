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
    
    public List<string> Tools { get; set; } = new();
    
    public List<AgentInputVariable> InputVariables { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AgentConfig
{
    // Deployment name (must match one of the configured deployments in AppHost)
    // Available options: gpt-4o-mini, gpt-4o, gpt-35-turbo, gpt-4-turbo
    public string Model { get; set; } = "gpt-4o-mini";
    
    public double Temperature { get; set; } = 0.7;
    
    public double TopP { get; set; } = 1.0;
    
    public int MaxTokens { get; set; } = 2000;
    
    public double FrequencyPenalty { get; set; } = 0.0;
    
    public double PresencePenalty { get; set; } = 0.0;
    
    public int MaxTurns { get; set; } = 10;
    
    public bool EnableMultiTurn { get; set; } = true;
    
    public string? GoalCompletionPrompt { get; set; }
}

public static class AvailableModels
{
    public static readonly List<ModelDeployment> All = new()
    {
        new ModelDeployment 
        { 
            DeploymentName = "gpt-4o-mini", 
            DisplayName = "GPT-4o Mini", 
            Description = "Fast and efficient for most tasks" 
        },
        new ModelDeployment 
        { 
            DeploymentName = "gpt-4o", 
            DisplayName = "GPT-4o", 
            Description = "More capable for complex reasoning" 
        },
        new ModelDeployment 
        { 
            DeploymentName = "gpt-35-turbo", 
            DisplayName = "GPT-3.5 Turbo", 
            Description = "Cost-effective for simple tasks" 
        },
        new ModelDeployment 
        { 
            DeploymentName = "gpt-4-turbo", 
            DisplayName = "GPT-4 Turbo", 
            Description = "Advanced reasoning capabilities" 
        }
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
