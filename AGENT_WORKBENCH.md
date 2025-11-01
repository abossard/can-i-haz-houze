# 🤖 AI Agent Workbench

## Overview

The Agent Workbench is a comprehensive platform for creating, configuring, and managing AI agents powered by Microsoft Semantic Kernel and Azure OpenAI. Build sophisticated multi-turn conversational agents with custom prompts, tool integrations, and workflow parameters.

## Features

### 🎯 Complete Agent Management
- **Create & Edit Agents**: Build AI agents with custom prompts and configurations
- **Multi-Model Support**: Choose from GPT-4o, GPT-4o Mini, GPT-3.5 Turbo, and GPT-4 Turbo
- **Tool Integration**: Connect agents to Ledger API, CRM API, and Documents API
- **Input Variables**: Parameterize prompts with dynamic variables

### 🔄 Multi-Turn Conversations
- **Iterative Execution**: Agents work autonomously towards defined goals
- **Goal Tracking**: Automatic completion detection using AI evaluation
- **Conversation History**: Full tracking of all turns and tool calls
- **Turn Limits**: Configurable maximum conversation turns (1-20)

### ⚙️ Advanced Configuration
- **Temperature Control**: Adjust response creativity (0.0 - 2.0)
- **Top P Sampling**: Fine-tune token selection probability
- **Max Tokens**: Control response length
- **Frequency & Presence Penalties**: Reduce repetition and improve diversity

### 🎛️ Background Processing
- **Long-Running Agents**: Execute agents asynchronously in the background
- **Pause & Resume**: Control running agents mid-conversation
- **Live Monitoring**: Real-time status updates with auto-refresh
- **Cancel Support**: Gracefully terminate running agents

### 📊 Execution Insights
- **Run History**: Track all agent executions
- **Conversation Visualization**: Color-coded turns with role badges
- **Tool Call Display**: Expandable cards showing tool invocations
- **JSON Formatting**: Pretty-printed arguments and results
- **Execution Logs**: Detailed logging for debugging

## User Interface

### Agent List Page (`/agents`)
The main dashboard showing all configured agents:
- Agent overview cards with name, model, and configuration
- Quick access to edit, run, and view history
- Create new agent button
- Search and filter capabilities

### Agent Editor (`/agent-editor/{id}`)
Comprehensive agent configuration interface:

**Prompt Section:**
- Large monospace textarea (12 rows) for prompt editing
- Live preview toggle to see formatted output
- Markdown formatting tips
- Support for {{variableName}} template syntax

**Model Selection:**
- Dropdown with all available OpenAI models
- Model descriptions to help choose the right one
- GPT-4o Mini: Fast and efficient for most tasks
- GPT-4o: Complex reasoning and problem-solving
- GPT-3.5 Turbo: Cost-effective for routine operations
- GPT-4 Turbo: Advanced capabilities for demanding workloads

**Multi-Turn Configuration:**
- Enable/disable multi-turn execution
- Maximum turns slider (1-20)
- Goal completion prompt for automatic stopping

**Workflow Parameters:**
- Temperature slider (0.0 - 2.0)
- Top P slider (0.0 - 1.0)
- Max tokens input (50 - 4000)
- Frequency penalty slider (-2.0 - 2.0)
- Presence penalty slider (-2.0 - 2.0)

**Tool Assignment:**
- Checkboxes for predefined tools:
  - ✅ Ledger API
  - ✅ CRM API
  - ✅ Documents API

**Input Variables:**
- Add/remove variable definitions
- Variable name and required flag
- Validation for proper {{}} syntax in prompts

### Agent Runner (`/agent-run/{id}`)
Execute agents and provide input variables:
- Dynamic form based on agent's input variables
- Required field validation
- Execute button for synchronous runs
- Queue for background button for async execution
- Clear instructions for variable inputs

### Active Agents Dashboard (`/active-agents`)
Real-time monitoring of running agents:
- Auto-refresh every 2 seconds
- Status display: Running, Paused, Canceling
- Turn progress: X/Y turns completed
- Goal achievement indicator
- Live log preview (last 5 logs)
- Control buttons:
  - ⏸️ Pause
  - ▶️ Resume
  - ❌ Cancel
- Latest conversation turn preview

### Run History Viewer (`/agent-runs/{agentId}`)
Browse and analyze past agent executions:

**Run List:**
- Chronological list of all executions
- Status badges (Completed, Failed, Running)
- Timestamp and duration
- Turn count and goal achievement
- Quick access to view details

**Run Details:**
- Full conversation history with color-coded roles:
  - ⚙️ **System** (gray): System prompts and instructions
  - 👤 **User** (blue): User inputs and requests
  - 🤖 **Assistant** (green): AI responses
  - 🔧 **Tool** (orange): Tool calls and results
- Tool call cards (collapsible):
  - Tool name badge
  - JSON-formatted arguments (indented)
  - JSON-formatted results (indented)
- Pre-formatted text display preserving whitespace
- Visual border styling for conversation flow
- Expandable execution logs section

## API Endpoints

### Configuration
- `GET /models` - Get available model deployments

### Agent Management
- `GET /agents` - List all agents
- `GET /agents/{id}` - Get specific agent
- `POST /agents` - Create new agent
- `PUT /agents/{id}` - Update agent
- `DELETE /agents/{id}` - Delete agent

### Synchronous Execution
- `POST /agents/{id}/run` - Execute agent (blocks until completion)
- `GET /agents/{agentId}/runs` - Get all runs for an agent
- `GET /runs/{agentId}/{id}` - Get specific run details

### Background Execution
- `POST /agents/{id}/run-async` - Queue agent for background execution
- `POST /runs/{agentId}/{id}/pause` - Pause a running agent
- `POST /runs/{agentId}/{id}/resume` - Resume a paused agent
- `POST /runs/{agentId}/{id}/cancel` - Cancel a running agent
- `GET /runs/active` - Get all currently active agent runs

## Example Usage

### Creating a Customer Service Agent

```json
{
  "name": "Customer Support Agent",
  "prompt": "You are a helpful customer support agent for a mortgage company. Help customer {{customerName}} with their inquiry about {{topic}}. Be professional and empathetic.",
  "config": {
    "model": "gpt-4o-mini",
    "temperature": 0.7,
    "topP": 0.9,
    "maxTokens": 500,
    "frequencyPenalty": 0.0,
    "presencePenalty": 0.0,
    "enableMultiTurn": true,
    "maxTurns": 5,
    "goalCompletionPrompt": "Resolve the customer's issue or provide a clear next step"
  },
  "tools": ["CRMAPI"],
  "inputVariables": [
    {"name": "customerName", "required": true},
    {"name": "topic", "required": true}
  ]
}
```

### Multi-Turn Analysis Agent

```json
{
  "name": "Document Analyzer",
  "prompt": "Analyze the mortgage documents for application {{applicationId}}. Review income verification, credit reports, employment records, and appraisal documents. Provide a comprehensive assessment.",
  "config": {
    "model": "gpt-4o",
    "temperature": 0.3,
    "enableMultiTurn": true,
    "maxTurns": 10,
    "goalCompletionPrompt": "Complete analysis with recommendation"
  },
  "tools": ["DocumentsAPI", "LedgerAPI"],
  "inputVariables": [
    {"name": "applicationId", "required": true}
  ]
}
```

### Quick Task Agent

```json
{
  "name": "Balance Checker",
  "prompt": "Check the account balance for customer {{customerId}} and format the result clearly.",
  "config": {
    "model": "gpt-35-turbo",
    "temperature": 0.0,
    "maxTokens": 100,
    "enableMultiTurn": false
  },
  "tools": ["LedgerAPI"],
  "inputVariables": [
    {"name": "customerId", "required": true}
  ]
}
```

## Architecture

### Technology Stack
- **Microsoft Semantic Kernel 1.33.0**: AI orchestration framework
- **Azure OpenAI**: GPT-4o, GPT-4o Mini, GPT-3.5 Turbo, GPT-4 Turbo
- **Azure Cosmos DB**: Single collection with `/agentId` partition key
- **.NET Aspire 9.3.1**: Cloud-native orchestration
- **Blazor Server**: Interactive web UI

### Data Models

**Agent:**
- Id: Unique identifier
- AgentId: Partition key (same as Id)
- EntityType: "agent"
- Name: Display name
- Prompt: System prompt template
- Config: AgentConfig object
- Tools: Array of tool names
- InputVariables: Array of variable definitions

**AgentRun:**
- Id: Unique identifier
- AgentId: Partition key (parent agent ID)
- EntityType: "agent-run"
- Status: Running, Paused, Completed, Failed, Cancelled
- ConversationHistory: Array of ConversationTurn objects
- TurnCount: Number of turns completed
- Goal: Goal completion prompt
- GoalAchieved: Boolean flag
- Logs: Array of execution log entries

**ConversationTurn:**
- TurnNumber: Sequential number
- Role: system, user, assistant, tool
- Content: Message content
- ToolCalls: Array of ToolCall objects (optional)
- Timestamp: When the turn occurred

**ToolCall:**
- Id: Tool call identifier
- Name: Tool name
- Arguments: JSON string
- Result: JSON string

### Storage Strategy
- **Single Container**: `agents` collection in Cosmos DB
- **Partition Key**: `/agentId` for all entities
- **Benefits**:
  - All related data (agent + runs + logs) in same partition
  - Transactional consistency within partition
  - Efficient queries with partition-scoped operations
  - Simplified management

### Background Processing
- **Channel-based Queue**: Non-blocking agent execution
- **Background Service**: Dedicated worker for processing queue
- **Cancellation Tokens**: Graceful shutdown support
- **Status Tracking**: Real-time updates in Cosmos DB

## Best Practices

### Prompt Engineering
- Use clear, specific instructions
- Define the agent's role and responsibilities
- Include formatting guidelines for outputs
- Leverage {{variables}} for dynamic content
- Test with various inputs

### Multi-Turn Configuration
- Start with lower turn limits (5-10)
- Write clear goal completion prompts
- Monitor conversation flow in history
- Adjust temperature based on task type

### Tool Selection
- Only assign necessary tools
- Understand what each tool provides
- Consider API rate limits
- Plan for error handling

### Model Selection
- **GPT-3.5 Turbo**: Simple tasks, high throughput needed
- **GPT-4o Mini**: Most general-purpose tasks, good balance
- **GPT-4o**: Complex reasoning, detailed analysis
- **GPT-4 Turbo**: Highest capability, demanding workloads

### Performance Optimization
- Use background execution for long-running agents
- Monitor active agents dashboard
- Set appropriate max tokens
- Use goal completion for early stopping

## Troubleshooting

### Agent Not Starting
- Check OpenAI connection configuration
- Verify model deployment exists
- Review execution logs
- Ensure input variables provided

### Tool Calls Failing
- Tool integration is UI placeholder only
- Actual tool execution not yet implemented
- Tool calls tracked but not executed

### Slow Execution
- Consider using faster model (GPT-4o Mini)
- Reduce max tokens
- Decrease turn limits
- Check network connectivity

### Conversation Not Stopping
- Review goal completion prompt
- Check if goal is achievable
- Verify max turns setting
- Monitor in active agents dashboard

## Future Enhancements

### Planned Features
- Actual tool plugin implementation
- Agent-as-tool functionality
- Function calling support
- Streaming responses
- SignalR real-time updates
- Export conversation history
- Agent templates library
- Usage analytics dashboard
- Multi-tenancy support

### Integration Opportunities
- Ledger API plugin with balance checks
- CRM API plugin for customer lookup
- Documents API plugin for document search
- Custom plugin development framework
- Agent composition and chaining

## Screenshots

> **Note**: Screenshots below show the Agent Workbench UI. Launch the application locally with `dotnet run --project src/CanIHazHouze.AppHost` to explore all features.

### Agent List Dashboard
*Overview of all configured AI agents with quick access to management functions*

### Agent Editor - Prompt Configuration
*Large monospace editor with live preview and markdown formatting support*

### Agent Editor - Model Selection
*Choose from multiple OpenAI models with descriptions*

### Agent Editor - Multi-Turn Settings
*Configure iterative execution with goal tracking and turn limits*

### Agent Editor - Workflow Parameters
*Fine-tune AI behavior with temperature, top P, penalties, and token limits*

### Agent Editor - Tool Assignment
*Select predefined tools for agent capabilities*

### Agent Editor - Input Variables
*Define dynamic variables for prompt templates*

### Agent Runner
*Execute agents with dynamic input forms*

### Active Agents Dashboard
*Real-time monitoring of running agents with pause/resume/cancel controls*

### Run History
*Browse past executions with status and timestamps*

### Run Details - Conversation History
*Color-coded turns showing full conversation flow*

### Run Details - Tool Call Visualization
*Collapsible cards with JSON-formatted tool interactions*

## Getting Started

1. **Start the Application**:
   ```bash
   cd src
   dotnet run --project CanIHazHouze.AppHost
   ```

2. **Access the Workbench**:
   - Navigate to the Web application from the Aspire dashboard
   - Click "🤖 Agent Workbench" in the navigation menu

3. **Create Your First Agent**:
   - Click "Create New Agent"
   - Enter a name and prompt
   - Select a model
   - Configure parameters
   - Add input variables if needed
   - Save

4. **Run the Agent**:
   - From the agent list, click "Run"
   - Provide values for input variables
   - Click "Execute Agent"
   - View results in run history

5. **Monitor Execution**:
   - Visit "Active Agents" for real-time monitoring
   - View conversation turns as they happen
   - Pause/resume/cancel as needed

6. **Analyze Results**:
   - Go to "Run History"
   - Click on any run to see details
   - Review full conversation history
   - Examine tool calls and results

## Learn More

- [Microsoft Semantic Kernel Documentation](https://learn.microsoft.com/en-us/semantic-kernel/)
- [Azure OpenAI Service](https://azure.microsoft.com/en-us/products/cognitive-services/openai-service)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Prompt Engineering Guide](https://platform.openai.com/docs/guides/prompt-engineering)

---

**Built with ❤️ using Microsoft Semantic Kernel and Azure OpenAI**
