# SSE Filtering Implementation Summary

## Overview
Implemented optional filtering for Server-Sent Events (SSE) streaming to allow flexible monitoring of agent runs. The streaming interface now supports filtering by agentId and runId, with the default behavior of streaming all events system-wide.

## Implementation Date
January 2025

## Architecture

### Three Streaming Modes

All streaming is handled through a single unified endpoint: `/stream`

1. **Stream Everything (Default)**
   - URL: `/stream`
   - Returns: All logs and conversation turns from all agent runs
   - Use case: System-wide monitoring, operations dashboard

2. **Filter by Agent**
   - URL: `/stream?agentId={agentId}`
   - Returns: All logs and conversation turns for the specified agent
   - Use case: Monitoring a specific agent type across multiple runs

3. **Filter by Specific Run**
   - URL: `/stream?agentId={agentId}&runId={runId}`
   - Returns: All logs and conversation turns for a specific run
   - Use case: Debugging a specific agent execution

## Files Modified

### 1. LogStreamService.cs
Added global streaming capabilities:

**New Interface Methods:**
```csharp
void RegisterGlobalLogStream(Func<string, string, AgentRunLog, Task> writeCallback);
void UnregisterGlobalLogStream(Func<string, string, AgentRunLog, Task> writeCallback);
void RegisterGlobalConversationStream(Func<string, string, ConversationTurn, Task> writeCallback);
void UnregisterGlobalConversationStream(Func<string, string, ConversationTurn, Task> writeCallback);
Task BroadcastLogWithContextAsync(string runId, string agentId, AgentRunLog log);
Task BroadcastConversationTurnWithContextAsync(string runId, string agentId, ConversationTurn turn);
```

**Key Changes:**
- Added `ConcurrentBag` collections for global callbacks
- Global callbacks receive runId and agentId parameters for filtering
- `BroadcastWithContextAsync` methods notify both specific and global listeners

### 2. Program.cs
Replaced the old specific-run endpoint with a unified filtered SSE endpoint:

**Endpoint:**
- Method: GET
- Path: `/stream`
- Query Parameters:
  - `agentId` (optional): Filter by agent ID
  - `runId` (optional): Filter by run ID
  - No parameters: Stream all runs

**Features:**
- Single endpoint handles all streaming scenarios
- Sends historical data for matching runs on connection
- Registers global callbacks with client-side filtering
- Automatically cleans up on disconnect
- Emits `connected`, `log`, `conversation`, and `status` events

**Migration:**
- Removed old endpoint: `/runs/{agentId}/{id}/stream`
- All functionality now available through `/stream` with query parameters

### 3. AgentExecutionService.cs
Updated to use new broadcast methods:

**Changes:**
- `AddLogAsync`: Now calls `BroadcastLogWithContextAsync(run.Id, run.AgentId, log)`
- `FunctionInvocationLoggingFilter.AddLogAndBroadcastAsync`: Now calls `BroadcastLogWithContextAsync`

### 4. MultiTurnAgentExecutor.cs
Updated conversation broadcasting:

**Changes:**
- `AddConversationTurnAsync`: Now calls `BroadcastConversationTurnWithContextAsync(run.Id, run.AgentId, turn)`

### 5. sse-test.html
Enhanced test page with filtering UI using the unified endpoint:

**New Features:**
- Radio buttons for mode selection (Stream All Runs / Filter by Agent / Specific Run)
- Dynamic form field enabling based on mode
- Shows runId and agentId context in all log and conversation entries
- Clear display button
- Improved help text explaining each streaming mode
- Emoji icons for better UX (🔌 for connect/disconnect, 🧹 for clear)

## Event Format

### Connected Event
```json
{
  "timestamp": "2025-01-08T10:30:00Z",
  "filters": {
    "agentId": "agent-123",
    "runId": "run-456"
  }
}
```

### Log Event
```json
{
  "runId": "run-456",
  "agentId": "agent-123",
  "log": {
    "timestamp": "2025-01-08T10:30:01Z",
    "level": "info",
    "message": "Tool call started: LedgerAPI.GetTransactions",
    "data": { ... }
  }
}
```

### Conversation Event
```json
{
  "runId": "run-456",
  "agentId": "agent-123",
  "turn": {
    "turnNumber": 1,
    "role": "assistant",
    "content": "I'll check the ledger for transactions.",
    "timestamp": "2025-01-08T10:30:02Z"
  }
}
```

### Status Event
```json
{
  "runId": "run-456",
  "agentId": "agent-123",
  "status": "completed",
  "turnCount": 3
}
```

## Usage Examples

### Example 1: Monitor All Runs
```javascript
const eventSource = new EventSource('/stream');
eventSource.addEventListener('log', (event) => {
  const { runId, agentId, log } = JSON.parse(event.data);
  console.log(`[${agentId}/${runId}] ${log.message}`);
});
```

### Example 2: Monitor Specific Agent
```javascript
const eventSource = new EventSource('/stream?agentId=mortgage-analyzer');
eventSource.addEventListener('conversation', (event) => {
  const { runId, turn } = JSON.parse(event.data);
  console.log(`Run ${runId}: ${turn.role}: ${turn.content}`);
});
```

### Example 3: Monitor Specific Run
```javascript
const agentId = 'mortgage-analyzer';
const runId = 'run-12345';
const eventSource = new EventSource(`/stream?agentId=${agentId}&runId=${runId}`);
```

## Benefits

1. **Flexible Monitoring**: Choose the level of detail needed - system-wide or targeted
2. **Production Ready**: Global streaming enables operations dashboards without knowing run IDs
3. **Backward Compatible**: Existing endpoint still works for specific run monitoring
4. **Efficient**: Client-side filtering reduces bandwidth and processing
5. **Real-time**: All events broadcast immediately to connected clients

## Testing

### Test Page Access
- URL: `https://localhost:7069/sse-test.html` (or your configured port)
- Select streaming mode using radio buttons
- Enter agent ID or run ID as needed
- Click "Connect" to start streaming
- All events display in real-time with context

### Manual Testing with curl
```bash
# Stream all runs
curl -N https://localhost:7069/stream

# Filter by agent
curl -N "https://localhost:7069/stream?agentId=mortgage-analyzer"

# Filter by run
curl -N "https://localhost:7069/stream?agentId=mortgage-analyzer&runId=run-12345"
```

## Performance Considerations

- **Global Callbacks**: All global listeners receive all events, filtering happens in callback
- **Memory**: Each connected client adds callbacks to concurrent collections
- **Cleanup**: Callbacks automatically removed on disconnect
- **Scalability**: For high-volume scenarios, consider filtering at service layer

## Future Enhancements

1. **Server-Side Filtering**: Move filtering logic to service layer for better performance
2. **Additional Filters**: Add filtering by status, date range, or log level
3. **Batching**: Batch events to reduce SSE overhead for high-frequency events
4. **Pagination**: Add pagination for historical data sent on connection
5. **WebSocket Option**: Consider WebSocket alternative for bidirectional communication

## Related Documentation

- [SSE Implementation Summary](./MCP_IMPLEMENTATION_SUMMARY.md) - Original SSE implementation
- [Agent Service Documentation](./CanIHazHouze.AgentService/README.md) - Service overview
- [MCP Usage Guide](./MCP_USAGE_GUIDE.md) - How to use MCP tools in agents
