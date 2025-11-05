using CanIHazHouze.AgentService.Configuration;
using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CanIHazHouze.AgentService.Services;

public class MultiTurnAgentExecutor
{
    private readonly IAgentStorageService _storageService;
    private readonly OpenAIConfiguration _openAIConfig;
    private readonly ILogger<MultiTurnAgentExecutor> _logger;
    private readonly IAgentEventBroadcaster _broadcaster;

    public MultiTurnAgentExecutor(
        IAgentStorageService storageService,
        IOptions<OpenAIConfiguration> openAIConfig,
        ILogger<MultiTurnAgentExecutor> logger,
        IAgentEventBroadcaster broadcaster)
    {
        _storageService = storageService;
        _openAIConfig = openAIConfig.Value;
        _logger = logger;
        _broadcaster = broadcaster;
    }
    
    private async Task AddConversationTurnAsync(AgentRun run, ConversationTurn turn)
    {
        run.ConversationHistory.Add(turn);
        await _broadcaster.BroadcastConversationAsync(run.Id, run.AgentId, turn);
    }
    
    private Kernel CreateKernelForModel(string deploymentName)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: _openAIConfig.Endpoint,
            apiKey: _openAIConfig.ApiKey);
        return builder.Build();
    }

    public async Task<AgentRun> ExecuteMultiTurnAsync(
        string agentId,
        Dictionary<string, string> inputValues,
        CancellationToken cancellationToken = default)
    {
        var run = new AgentRun
        {
            AgentId = agentId,
            InputValues = inputValues,
            Status = "running"
        };

        try
        {
            run = await _storageService.CreateRunAsync(run);
            await AddLogAsync(run, "info", "Multi-turn agent execution started");

            var agent = await _storageService.GetAgentAsync(agentId);
            if (agent == null)
            {
                throw new InvalidOperationException($"Agent {agentId} not found");
            }

            await AddLogAsync(run, "info", $"Loaded agent: {agent.Name}");

            // Validate required input variables
            foreach (var inputVar in agent.InputVariables.Where(v => v.Required))
            {
                if (!inputValues.ContainsKey(inputVar.Name) || string.IsNullOrWhiteSpace(inputValues[inputVar.Name]))
                {
                    throw new InvalidOperationException($"Required input variable '{inputVar.Name}' is missing");
                }
            }

            // Build the initial system prompt with input variables
            var systemPrompt = agent.Prompt;
            foreach (var kvp in inputValues)
            {
                systemPrompt = systemPrompt.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
            }

            // Set goal if provided in config
            if (!string.IsNullOrEmpty(agent.Config.GoalCompletionPrompt))
            {
                run.Goal = agent.Config.GoalCompletionPrompt;
            }

            run.MaxTurns = agent.Config.MaxTurns;
            await _storageService.UpdateRunAsync(run);

            await AddLogAsync(run, "info", $"Starting multi-turn conversation with model: {agent.Config.Model}");

            // Create kernel with the agent's specified model deployment
            var kernel = CreateKernelForModel(agent.Config.Model);

            // Configure execution settings
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = agent.Config.Temperature,
                TopP = agent.Config.TopP,
                MaxTokens = agent.Config.MaxTokens,
                FrequencyPenalty = agent.Config.FrequencyPenalty,
                PresencePenalty = agent.Config.PresencePenalty
            };

            var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            
            // Add system message
            chatHistory.AddSystemMessage(systemPrompt);
            await AddConversationTurnAsync(run, new ConversationTurn
            {
                TurnNumber = 0,
                Role = "system",
                Content = systemPrompt
            });

            // Multi-turn execution loop
            while (run.TurnCount < run.MaxTurns && !run.GoalAchieved && !cancellationToken.IsCancellationRequested)
            {
                run.TurnCount++;
                await AddLogAsync(run, "info", $"Starting turn {run.TurnCount}/{run.MaxTurns}");

                // Check if paused
                var currentRun = await _storageService.GetRunAsync(run.Id, run.AgentId);
                if (currentRun?.Status == "paused")
                {
                    await AddLogAsync(run, "info", "Execution paused by user");
                    run.Status = "paused";
                    run.PausedAt = DateTime.UtcNow;
                    await _storageService.UpdateRunAsync(run);
                    return run;
                }

                try
                {
                    // Generate assistant response
                    var response = await chatCompletion.GetChatMessageContentAsync(
                        chatHistory,
                        executionSettings,
                        kernel,
                        cancellationToken);

                    var assistantMessage = response.Content ?? string.Empty;
                    chatHistory.AddAssistantMessage(assistantMessage);
                    
                    await AddConversationTurnAsync(run, new ConversationTurn
                    {
                        TurnNumber = run.TurnCount,
                        Role = "assistant",
                        Content = assistantMessage
                    });

                    await AddLogAsync(run, "info", $"Turn {run.TurnCount}: Generated response ({assistantMessage.Length} chars)");

                    // Check if goal is achieved
                    if (!string.IsNullOrEmpty(run.Goal))
                    {
                        var goalCheck = await CheckGoalAchievedAsync(chatCompletion, chatHistory, run.Goal, executionSettings, cancellationToken);
                        if (goalCheck)
                        {
                            run.GoalAchieved = true;
                            await AddLogAsync(run, "info", "Goal achieved! Completing execution");
                            break;
                        }
                    }

                    // Add a follow-up user message to continue the conversation
                    if (run.TurnCount < run.MaxTurns && !run.GoalAchieved)
                    {
                        var continueMessage = "Continue working towards the goal.";
                        chatHistory.AddUserMessage(continueMessage);
                        await AddConversationTurnAsync(run, new ConversationTurn
                        {
                            TurnNumber = run.TurnCount,
                            Role = "user",
                            Content = continueMessage
                        });
                    }

                    // Update run status periodically
                    run.LastUpdated = DateTime.UtcNow;
                    await _storageService.UpdateRunAsync(run);
                }
                catch (Exception ex)
                {
                    await AddLogAsync(run, "error", $"Error in turn {run.TurnCount}: {ex.Message}");
                    throw;
                }
            }

            // Determine completion reason
            if (run.GoalAchieved)
            {
                run.Result = "Goal achieved";
                await AddLogAsync(run, "info", $"Agent completed successfully after {run.TurnCount} turns - goal achieved");
            }
            else if (run.TurnCount >= run.MaxTurns)
            {
                run.Result = "Max turns reached";
                await AddLogAsync(run, "info", $"Agent completed after reaching max turns ({run.MaxTurns})");
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                run.Result = "Cancelled by user";
                await AddLogAsync(run, "info", "Agent execution cancelled");
            }

            run.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Multi-turn agent {AgentId} completed for run {RunId} after {TurnCount} turns",
                LogSanitizer.Sanitize(agentId),
                LogSanitizer.Sanitize(run.Id),
                run.TurnCount);
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.Error = ex.Message;
            run.CompletedAt = DateTime.UtcNow;

            await AddLogAsync(run, "error", $"Agent execution failed: {ex.Message}");

            _logger.LogError(ex, "Multi-turn agent {AgentId} failed for run {RunId}",
                LogSanitizer.Sanitize(agentId),
                LogSanitizer.Sanitize(run.Id));
        }

        await _storageService.UpdateRunAsync(run);
        return run;
    }

    private async Task<bool> CheckGoalAchievedAsync(
        IChatCompletionService chatCompletion,
        ChatHistory chatHistory,
        string goal,
        OpenAIPromptExecutionSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create a temporary chat to evaluate goal completion
            var evaluationChat = new ChatHistory();
            evaluationChat.AddSystemMessage($"You are evaluating if a goal has been achieved. The goal is: {goal}");
            evaluationChat.AddUserMessage($"Based on the following conversation, has the goal been achieved? Answer only 'yes' or 'no'.\n\nConversation:\n{string.Join("\n", chatHistory.Select(m => $"{m.Role}: {m.Content}"))}");

            var evaluation = await chatCompletion.GetChatMessageContentAsync(
                evaluationChat,
                settings,
                cancellationToken: cancellationToken);

            var response = evaluation.Content?.ToLower().Trim() ?? "";
            return response.Contains("yes");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check goal achievement, assuming not achieved");
            return false;
        }
    }

    private async Task AddLogAsync(AgentRun run, string level, string message)
    {
        run.Logs.Add(new AgentRunLog
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
        
        // Only persist every few logs to avoid too many writes
        if (run.Logs.Count % 5 == 0)
        {
            await _storageService.UpdateRunAsync(run);
        }
    }
}
