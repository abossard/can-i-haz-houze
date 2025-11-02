using System.Threading.Channels;
using CanIHazHouze.AgentService.Services;

namespace CanIHazHouze.AgentService.BackgroundServices;

public class AgentExecutionRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public Dictionary<string, string> InputValues { get; set; } = new();
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
}

public class AgentExecutionBackgroundService : BackgroundService
{
    private readonly Channel<AgentExecutionRequest> _executionQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentExecutionBackgroundService> _logger;
    private readonly Dictionary<string, AgentExecutionRequest> _runningAgents = new();
    private readonly object _lock = new();

    public AgentExecutionBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AgentExecutionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _executionQueue = Channel.CreateUnbounded<AgentExecutionRequest>();
    }

    public async Task<string> QueueAgentExecutionAsync(string agentId, Dictionary<string, string> inputValues)
    {
        var request = new AgentExecutionRequest
        {
            AgentId = agentId,
            RunId = Guid.NewGuid().ToString(),
            InputValues = inputValues
        };

        await _executionQueue.Writer.WriteAsync(request);
        _logger.LogInformation("Queued agent {AgentId} for execution with run ID {RunId}", agentId, request.RunId);
        
        return request.RunId;
    }

    public List<string> GetRunningAgentIds()
    {
        lock (_lock)
        {
            return _runningAgents.Keys.ToList();
        }
    }

    public bool PauseAgent(string runId)
    {
        lock (_lock)
        {
            if (_runningAgents.TryGetValue(runId, out var request))
            {
                // The actual pause logic is handled in the executor by checking status
                _logger.LogInformation("Pause requested for run {RunId}", runId);
                return true;
            }
        }
        return false;
    }

    public bool ResumeAgent(string runId)
    {
        lock (_lock)
        {
            if (_runningAgents.ContainsKey(runId))
            {
                _logger.LogInformation("Resume requested for run {RunId}", runId);
                return true;
            }
        }
        return false;
    }

    public bool CancelAgent(string runId)
    {
        lock (_lock)
        {
            if (_runningAgents.TryGetValue(runId, out var request))
            {
                request.CancellationTokenSource.Cancel();
                _logger.LogInformation("Cancellation requested for run {RunId}", runId);
                return true;
            }
        }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent Execution Background Service started");

        await foreach (var request in _executionQueue.Reader.ReadAllAsync(stoppingToken))
        {
            // Start execution in a separate task
            _ = Task.Run(async () => await ExecuteAgentAsync(request, stoppingToken), stoppingToken);
        }
    }

    private async Task ExecuteAgentAsync(AgentExecutionRequest request, CancellationToken stoppingToken)
    {
        lock (_lock)
        {
            _runningAgents[request.RunId] = request;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<MultiTurnAgentExecutor>();
            var storageService = scope.ServiceProvider.GetRequiredService<IAgentStorageService>();

            _logger.LogInformation("Starting execution of agent {AgentId} with run ID {RunId}",
                request.AgentId, request.RunId);

            // Create a linked token that responds to both the request cancellation and service stopping
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                request.CancellationTokenSource.Token,
                stoppingToken);

            var run = await executor.ExecuteMultiTurnAsync(
                request.AgentId,
                request.InputValues,
                linkedCts.Token);

            _logger.LogInformation("Completed execution of agent {AgentId} with run ID {RunId}. Status: {Status}",
                request.AgentId, request.RunId, run.Status);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent execution {RunId} was cancelled", request.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing agent {AgentId} with run ID {RunId}",
                request.AgentId, request.RunId);
        }
        finally
        {
            lock (_lock)
            {
                _runningAgents.Remove(request.RunId);
            }
        }
    }
}
