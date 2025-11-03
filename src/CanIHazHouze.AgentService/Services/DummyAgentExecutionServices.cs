using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Services;
using Microsoft.Extensions.Options;
using CanIHazHouze.AgentService.Configuration;

namespace CanIHazHouze.AgentService.Services;

/// <summary>
/// Dummy single-turn execution service used when no real OpenAI endpoint is configured.
/// Produces deterministic, low-effort responses for tests/local runs without secrets.
/// </summary>
public class DummyAgentExecutionService : IAgentExecutionService
{
    private readonly IAgentStorageService _storage;
    private readonly ILogger<DummyAgentExecutionService> _logger;

    public DummyAgentExecutionService(IAgentStorageService storage, ILogger<DummyAgentExecutionService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<AgentRun> ExecuteAgentAsync(string agentId, Dictionary<string, string> inputValues)
    {
        var run = new AgentRun
        {
            AgentId = agentId,
            InputValues = inputValues,
            Status = "completed",
            CompletedAt = DateTime.UtcNow,
            Result = $"Dummy response for agent {agentId}. Inputs: " + string.Join(", ", inputValues.Select(kv => kv.Key + "=" + kv.Value))
        };

        run.Logs.Add(new AgentRunLog { Level = "info", Message = "Dummy execution started" });
        run.Logs.Add(new AgentRunLog { Level = "info", Message = "Dummy execution finished" });

        run = await _storage.CreateRunAsync(run);
        await _storage.UpdateRunAsync(run);

        _logger.LogInformation("DummyAgentExecutionService returned deterministic result for agent {AgentId}", agentId);
        return run;
    }
}

/// <summary>
/// Dummy multi-turn executor when OpenAI endpoint missing. Simulates a few turns then completes.
/// </summary>
public class DummyMultiTurnAgentExecutor : MultiTurnAgentExecutor
{
    private readonly IAgentStorageService _storage;
    private readonly ILogger<DummyMultiTurnAgentExecutor> _logger;

    public DummyMultiTurnAgentExecutor(IAgentStorageService storage, IOptions<OpenAIConfiguration> cfg, ILogger<DummyMultiTurnAgentExecutor> logger)
        : base(storage, Options.Create(cfg.Value), logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<AgentRun> ExecuteMultiTurnDummyAsync(string agentId, Dictionary<string, string> inputValues, CancellationToken ct = default)
    {
        var run = new AgentRun
        {
            AgentId = agentId,
            InputValues = inputValues,
            Status = "running",
            MaxTurns = 3
        };
        run = await _storage.CreateRunAsync(run);

        for (int i = 1; i <= run.MaxTurns; i++)
        {
            if (ct.IsCancellationRequested) break;
            run.TurnCount = i;
            run.ConversationHistory.Add(new ConversationTurn { TurnNumber = i, Role = "assistant", Content = $"Dummy turn {i} for agent {agentId}" });
            run.Logs.Add(new AgentRunLog { Level = "info", Message = $"Dummy turn {i} generated" });
        }

        run.Status = "completed";
        run.Result = "Dummy multi-turn conversation complete";
        run.CompletedAt = DateTime.UtcNow;
        await _storage.UpdateRunAsync(run);
        _logger.LogInformation("DummyMultiTurnAgentExecutor completed dummy run {RunId} for agent {AgentId}", run.Id, agentId);
        return run;
    }
}
