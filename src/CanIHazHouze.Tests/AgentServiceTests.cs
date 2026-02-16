using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.Tests;

public class AgentServiceTests
{
    [Fact]
    public void Agent_HasDefaultValues()
    {
        // Arrange & Act
        var agent = new Agent();

        // Assert
        Assert.NotNull(agent.Id);
        Assert.NotEmpty(agent.Id);
        Assert.NotNull(agent.Config);
        Assert.NotNull(agent.Tools);
        Assert.NotNull(agent.InputVariables);
        Assert.Equal("gpt-41-mini", agent.Config.Model);
        Assert.Equal(0.7, agent.Config.Temperature);
        Assert.Equal(1.0, agent.Config.TopP);
        Assert.Equal(2000, agent.Config.MaxTokens);
    }

    [Fact]
    public void AgentConfig_HasCorrectDefaults()
    {
        // Arrange & Act
        var config = new AgentConfig();

        // Assert
        Assert.Equal("gpt-41-mini", config.Model);
        Assert.Equal(0.7, config.Temperature);
        Assert.Equal(1.0, config.TopP);
        Assert.Equal(2000, config.MaxTokens);
        Assert.Equal(0.0, config.FrequencyPenalty);
        Assert.Equal(0.0, config.PresencePenalty);
    }

    [Fact]
    public void AvailableModels_IncludesGpt5Nano()
    {
        // Arrange
        var deploymentNames = AvailableModels.All.Select(m => m.DeploymentName).ToList();

        // Assert
        Assert.Contains("gpt-5-nano", deploymentNames);
    }

    [Fact]
    public void AgentRun_HasDefaultStatus()
    {
        // Arrange & Act
        var run = new AgentRun();

        // Assert
        Assert.NotNull(run.Id);
        Assert.NotEmpty(run.Id);
        Assert.Equal("pending", run.Status);
        Assert.NotNull(run.InputValues);
        Assert.NotNull(run.Logs);
        Assert.Empty(run.Logs);
    }

    [Fact]
    public void AgentInputVariable_RequiredByDefault()
    {
        // Arrange & Act
        var variable = new AgentInputVariable();

        // Assert
        Assert.True(variable.Required);
    }

    [Fact]
    public void Agent_CanAddTools()
    {
        // Arrange
        var agent = new Agent();

        // Act
        agent.Tools.Add("ledger-api");
        agent.Tools.Add("crm-api");

        // Assert
        Assert.Equal(2, agent.Tools.Count);
        Assert.Contains("ledger-api", agent.Tools);
        Assert.Contains("crm-api", agent.Tools);
    }

    [Fact]
    public void Agent_CanAddInputVariables()
    {
        // Arrange
        var agent = new Agent();

        // Act
        agent.InputVariables.Add(new AgentInputVariable 
        { 
            Name = "customerName", 
            Description = "The customer's name",
            Required = true 
        });

        // Assert
        Assert.Single(agent.InputVariables);
        Assert.Equal("customerName", agent.InputVariables[0].Name);
        Assert.True(agent.InputVariables[0].Required);
    }

    [Fact]
    public void AgentRun_CanAddLogs()
    {
        // Arrange
        var run = new AgentRun();

        // Act
        run.Logs.Add(new AgentRunLog
        {
            Level = "info",
            Message = "Agent execution started"
        });
        run.Logs.Add(new AgentRunLog
        {
            Level = "error",
            Message = "An error occurred"
        });

        // Assert
        Assert.Equal(2, run.Logs.Count);
        Assert.Equal("info", run.Logs[0].Level);
        Assert.Equal("error", run.Logs[1].Level);
    }

    // TODO: Add integration tests with Cosmos DB emulator
    // TODO: Add integration tests with Semantic Kernel mock
}
