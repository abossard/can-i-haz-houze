# Can I Haz Houze - Architecture Documentation

> A mortgage approval system built with .NET 9.0 and .NET Aspire 9.3.1

## High-Level Architecture Overview

```mermaid
flowchart TB
    subgraph External["☁️ Azure Services"]
        CosmosDB[(Azure Cosmos DB)]
        BlobStorage[(Azure Blob Storage)]
        OpenAI[Azure OpenAI]
    end

    subgraph Orchestration["🎯 .NET Aspire Orchestration"]
        AppHost[AppHost<br/>Orchestrator]
    end

    subgraph Services["🔧 Microservices"]
        DocSvc[DocumentService<br/>📄]
        LedgerSvc[LedgerService<br/>💰]
        MortgageSvc[MortgageApprover<br/>🏠]
        CrmSvc[CrmService<br/>👥]
        AgentSvc[AgentService<br/>🤖]
    end

    subgraph Frontend["🌐 Frontend"]
        Web[Web Frontend<br/>Blazor Server]
    end

    AppHost --> DocSvc
    AppHost --> LedgerSvc
    AppHost --> MortgageSvc
    AppHost --> CrmSvc
    AppHost --> AgentSvc
    AppHost --> Web

    Web --> DocSvc
    Web --> LedgerSvc
    Web --> MortgageSvc
    Web --> CrmSvc
    Web --> AgentSvc

    MortgageSvc --> DocSvc
    MortgageSvc --> LedgerSvc
    
    AgentSvc --> DocSvc
    AgentSvc --> LedgerSvc
    AgentSvc --> CrmSvc

    DocSvc --> CosmosDB
    DocSvc --> BlobStorage
    DocSvc --> OpenAI
    LedgerSvc --> CosmosDB
    MortgageSvc --> CosmosDB
    CrmSvc --> CosmosDB
    AgentSvc --> CosmosDB
    AgentSvc --> OpenAI
```

## Project Structure

```mermaid
graph LR
    subgraph Solution["CanIHazHouze.sln"]
        AppHost[CanIHazHouze.AppHost]
        ServiceDefaults[CanIHazHouze.ServiceDefaults]
        DocSvc[CanIHazHouze.DocumentService]
        LedgerSvc[CanIHazHouze.LedgerService]
        MortgageSvc[CanIHazHouze.MortgageApprover]
        CrmSvc[CanIHazHouze.CrmService]
        AgentSvc[CanIHazHouze.AgentService]
        Web[CanIHazHouze.Web]
        Tests[CanIHazHouze.Tests]
    end

    DocSvc --> ServiceDefaults
    LedgerSvc --> ServiceDefaults
    MortgageSvc --> ServiceDefaults
    CrmSvc --> ServiceDefaults
    AgentSvc --> ServiceDefaults
    Web --> ServiceDefaults

    AppHost -.->|orchestrates| DocSvc
    AppHost -.->|orchestrates| LedgerSvc
    AppHost -.->|orchestrates| MortgageSvc
    AppHost -.->|orchestrates| CrmSvc
    AppHost -.->|orchestrates| AgentSvc
    AppHost -.->|orchestrates| Web
```

## Service Details

### ServiceDefaults (Shared Library)

```mermaid
classDiagram
    class Extensions {
        +AddServiceDefaults() TBuilder
        +AddMCPSupport() IMcpServerBuilder
        +ConfigureOpenTelemetry() TBuilder
        +AddDefaultHealthChecks() TBuilder
        +AddOpenApiWithAzureContainerAppsServers() void
        +MapDefaultEndpoints() IEndpointRouteBuilder
    }
    
    class ServiceDiscovery {
        <<package>>
        HTTP Client Service Discovery
        Standard Resilience Handler
    }
    
    class OpenTelemetry {
        <<package>>
        Metrics
        Tracing
        Logging
    }
    
    class MCPSupport {
        <<package>>
        ModelContextProtocol
        HTTP/SSE Transport
    }
    
    Extensions --> ServiceDiscovery
    Extensions --> OpenTelemetry
    Extensions --> MCPSupport
```

### DocumentService

```mermaid
classDiagram
    class DocumentService {
        <<service>>
    }
    
    class IDocumentService {
        <<interface>>
        +GetDocumentsAsync(owner) Task~IEnumerable~DocumentMeta~~
        +GetDocumentAsync(id, owner) Task~DocumentMeta~
        +UploadDocumentAsync(...) Task~DocumentMeta~
        +UpdateDocumentTagsAsync(...) Task~DocumentMeta~
        +DeleteDocumentAsync(...) Task~bool~
    }
    
    class DocumentServiceImpl {
        -CosmosClient _cosmosClient
        -BlobServiceClient _blobClient
        +GetDocumentsAsync() Task
        +UploadDocumentAsync() Task
    }
    
    class IDocumentAIService {
        <<interface>>
        +ExtractMetadataAsync() Task~DocumentMetadata~
        +GenerateSummaryAsync() Task~string~
        +SuggestTagsAsync() Task~List~string~~
    }
    
    class DocumentAIService {
        -AzureOpenAIClient _openAIClient
        +ExtractMetadataAsync() Task
        +GenerateSummaryAsync() Task
    }
    
    class DocumentTools {
        <<MCP Tools>>
        +ListDocuments(owner) IEnumerable~DocumentMeta~
        +GetDocument(id, owner) DocumentMeta
        +UpdateDocumentTags(...) DocumentMeta
        +DeleteDocument(...) bool
        +VerifyMortgageDocuments(owner) DocumentVerificationResult
    }
    
    class DocumentMeta {
        +Id: Guid
        +Owner: string
        +FileName: string
        +ContentType: string
        +Size: long
        +Tags: List~string~
        +AiMetadata: DocumentMetadata
    }
    
    class DocumentMetadata {
        +DocumentType: string
        +Summary: string
        +Entities: Dictionary
        +Dates: List~DateExtraction~
        +Amounts: List~AmountExtraction~
        +SuggestedTags: List~string~
        +ConfidenceScore: double
    }
    
    IDocumentService <|.. DocumentServiceImpl
    IDocumentAIService <|.. DocumentAIService
    DocumentService --> IDocumentService
    DocumentService --> IDocumentAIService
    DocumentTools --> IDocumentService
    DocumentTools --> IDocumentAIService
    DocumentServiceImpl --> DocumentMeta
    DocumentAIService --> DocumentMetadata
```

### LedgerService

```mermaid
classDiagram
    class LedgerService {
        <<service>>
    }
    
    class ILedgerService {
        <<interface>>
        +GetAccountAsync(owner) Task~AccountInfo~
        +UpdateBalanceAsync(...) Task~AccountInfo~
        +GetTransactionsAsync(owner) Task~IEnumerable~Transaction~~
        +CheckSufficientFundsAsync(...) Task~bool~
    }
    
    class LedgerServiceImpl {
        -CosmosClient _cosmosClient
        +GetAccountAsync() Task
        +UpdateBalanceAsync() Task
    }
    
    class LedgerTools {
        <<MCP Tools>>
        +GetAccount(owner) AccountInfo
        +UpdateBalance(...) AccountInfo
        +GetTransactions(owner) IEnumerable~Transaction~
        +CheckSufficientFunds(...) FundsCheckResult
        +CalculateMortgagePayment(...) MortgageCalculation
    }
    
    class AccountInfo {
        +Owner: string
        +Balance: decimal
        +CreatedAt: DateTime
        +LastUpdatedAt: DateTime
    }
    
    class Transaction {
        +Id: string
        +Owner: string
        +Amount: decimal
        +Description: string
        +Timestamp: DateTime
    }
    
    ILedgerService <|.. LedgerServiceImpl
    LedgerService --> ILedgerService
    LedgerTools --> ILedgerService
    LedgerServiceImpl --> AccountInfo
    LedgerServiceImpl --> Transaction
```

### MortgageApprover

```mermaid
classDiagram
    class MortgageApprover {
        <<service>>
    }
    
    class IMortgageApprovalService {
        <<interface>>
        +CreateMortgageRequestAsync(userName) Task~MortgageRequest~
        +GetMortgageRequestAsync(requestId) Task~MortgageRequest~
        +SubmitRequirementAsync(...) Task~MortgageRequest~
        +ProcessApprovalAsync(requestId) Task~MortgageRequest~
    }
    
    class MortgageApprovalServiceImpl {
        -CosmosClient _cosmosClient
        +CreateMortgageRequestAsync() Task
        +ProcessApprovalAsync() Task
    }
    
    class IDocumentVerificationService {
        <<interface>>
        +VerifyDocumentsAsync(userName) Task~DocumentVerificationResult~
    }
    
    class ILedgerVerificationService {
        <<interface>>
        +VerifyFundsAsync(userName, amount) Task~FundsVerificationResult~
    }
    
    class ICrossServiceVerificationService {
        <<interface>>
        +VerifyAllAsync(userName, loanAmount) Task~CrossServiceVerificationResult~
    }
    
    class DocumentVerificationService {
        -HttpClient _httpClient
    }
    
    class LedgerVerificationService {
        -HttpClient _httpClient
    }
    
    class CrossServiceVerificationService {
        -IDocumentVerificationService _docVerification
        -ILedgerVerificationService _ledgerVerification
    }
    
    class MortgageTools {
        <<MCP Tools>>
        +CreateMortgageRequest(userName) MortgageRequest
        +GetMortgageRequest(requestId) MortgageRequest
        +SubmitIncomeData(...) MortgageRequest
        +SubmitCreditData(...) MortgageRequest
        +SubmitEmploymentData(...) MortgageRequest
        +SubmitPropertyData(...) MortgageRequest
        +ProcessApproval(requestId) MortgageRequest
    }
    
    class MortgageRequest {
        +RequestId: Guid
        +UserName: string
        +Status: MortgageStatus
        +RequestData: MortgageRequestData
        +CreatedAt: DateTime
    }
    
    class MortgageRequestData {
        +Income: MortgageIncomeData
        +Credit: MortgageCreditData
        +Employment: MortgageEmploymentData
        +Property: MortgagePropertyData
    }
    
    IMortgageApprovalService <|.. MortgageApprovalServiceImpl
    IDocumentVerificationService <|.. DocumentVerificationService
    ILedgerVerificationService <|.. LedgerVerificationService
    ICrossServiceVerificationService <|.. CrossServiceVerificationService
    
    MortgageApprover --> IMortgageApprovalService
    MortgageApprover --> ICrossServiceVerificationService
    MortgageTools --> IMortgageApprovalService
    CrossServiceVerificationService --> IDocumentVerificationService
    CrossServiceVerificationService --> ILedgerVerificationService
    MortgageApprovalServiceImpl --> MortgageRequest
    MortgageRequest --> MortgageRequestData
```

### CrmService

```mermaid
classDiagram
    class CrmService {
        <<service>>
    }
    
    class ICrmService {
        <<interface>>
        +CreateComplaintAsync(...) Task~Complaint~
        +GetComplaintsAsync(customerName) Task~IEnumerable~Complaint~~
        +GetComplaintAsync(id) Task~Complaint~
        +AddCommentAsync(...) Task~Complaint~
        +UpdateStatusAsync(...) Task~Complaint~
        +RequestApprovalAsync(...) Task~Complaint~
    }
    
    class CrmServiceImpl {
        -CosmosClient _cosmosClient
        +CreateComplaintAsync() Task
        +GetComplaintsAsync() Task
    }
    
    class CrmTools {
        <<MCP Tools>>
        +CreateComplaint(...) Complaint
        +GetComplaints(customerName) IEnumerable~Complaint~
        +GetComplaint(id) Complaint
        +AddComment(...) Complaint
        +UpdateStatus(...) Complaint
        +RequestApproval(...) Complaint
    }
    
    class Complaint {
        +Id: Guid
        +CustomerName: string
        +Title: string
        +Description: string
        +Status: ComplaintStatus
        +Comments: List~Comment~
        +Approvals: List~ApprovalRequest~
    }
    
    class Comment {
        +Id: Guid
        +Author: string
        +Content: string
        +Timestamp: DateTime
    }
    
    ICrmService <|.. CrmServiceImpl
    CrmService --> ICrmService
    CrmTools --> ICrmService
    CrmServiceImpl --> Complaint
    Complaint --> Comment
```

### AgentService

```mermaid
classDiagram
    class AgentService {
        <<service>>
    }
    
    class IAgentStorageService {
        <<interface>>
        +GetAllAgentsAsync() Task~List~Agent~~
        +GetAgentAsync(id) Task~Agent~
        +CreateAgentAsync(agent) Task~Agent~
        +UpdateAgentAsync(agent) Task~Agent~
        +DeleteAgentAsync(id) Task~bool~
        +SaveRunAsync(run) Task~AgentRun~
        +GetRunsAsync(agentId) Task~List~AgentRun~~
    }
    
    class AgentStorageService {
        -CosmosClient _cosmosClient
    }
    
    class IAgentExecutionService {
        <<interface>>
        +ExecuteAgentAsync(agentId, inputs) Task~AgentRun~
        +CancelRunAsync(runId) Task~bool~
    }
    
    class AgentExecutionService {
        -IAgentStorageService _storageService
        -AzureOpenAIClient _openAIClient
        -IMcpClientService _mcpClientService
        -IAgentEventBroadcaster _broadcaster
    }
    
    class MultiTurnAgentExecutor {
        -IAgentExecutionService _executionService
        +ExecuteMultiTurnAsync() Task~AgentRun~
    }
    
    class IMcpClientService {
        <<interface>>
        +GetToolsAsync(serviceUrl) Task~List~Tool~~
        +InvokeToolAsync(...) Task~object~
    }
    
    class McpClientService {
        -HttpClient _httpClient
    }
    
    class IAgentEventBroadcaster {
        <<interface>>
        +BroadcastLogAsync(...) Task
        +BroadcastStatusAsync(...) Task
    }
    
    class AgentEventBroadcaster {
        -IHubContext _hubContext
    }
    
    class AgentHub {
        <<SignalR Hub>>
        +JoinAgentGroup(agentId) Task
        +LeaveAgentGroup(agentId) Task
    }
    
    class AgentTools {
        <<MCP Tools>>
        +ListAgents() List~Agent~
        +GetAgent(id) Agent
        +CreateAgent(...) Agent
        +ExecuteAgent(id, inputs) AgentRun
        +GetRuns(agentId) List~AgentRun~
    }
    
    class Agent {
        +Id: string
        +Name: string
        +Description: string
        +Prompt: string
        +Config: AgentConfig
        +Tools: List~string~
        +InputVariables: List~AgentInputVariable~
    }
    
    class AgentConfig {
        +Model: string
        +Temperature: double
        +MaxTokens: int
        +MaxTurns: int
        +EnableMultiTurn: bool
    }
    
    class AgentRun {
        +Id: string
        +AgentId: string
        +Status: AgentRunStatus
        +Inputs: Dictionary
        +Output: string
        +Logs: List~AgentRunLog~
    }
    
    IAgentStorageService <|.. AgentStorageService
    IAgentExecutionService <|.. AgentExecutionService
    IMcpClientService <|.. McpClientService
    IAgentEventBroadcaster <|.. AgentEventBroadcaster
    
    AgentService --> IAgentStorageService
    AgentService --> IAgentExecutionService
    AgentExecutionService --> IMcpClientService
    AgentExecutionService --> IAgentEventBroadcaster
    AgentTools --> IAgentStorageService
    AgentTools --> IAgentExecutionService
    AgentStorageService --> Agent
    AgentStorageService --> AgentRun
    Agent --> AgentConfig
```

### Web Frontend

```mermaid
classDiagram
    class WebFrontend {
        <<Blazor Server>>
    }
    
    class DocumentApiClient {
        -HttpClient _httpClient
        +GetDocumentsAsync(owner) Task~List~DocumentMeta~~
        +UploadDocumentAsync(...) Task~DocumentMeta~
        +DeleteDocumentAsync(...) Task~bool~
    }
    
    class LedgerApiClient {
        -HttpClient _httpClient
        +GetAccountAsync(owner) Task~AccountInfo~
        +UpdateBalanceAsync(...) Task~AccountInfo~
        +GetTransactionsAsync(owner) Task~List~Transaction~~
    }
    
    class MortgageApiClient {
        -HttpClient _httpClient
        +CreateMortgageRequestAsync(userName) Task~MortgageRequest~
        +GetMortgageRequestAsync(requestId) Task~MortgageRequest~
        +SubmitRequirementAsync(...) Task~MortgageRequest~
    }
    
    class CrmApiClient {
        -HttpClient _httpClient
        +GetComplaintsAsync(customerName) Task~List~Complaint~~
        +CreateComplaintAsync(...) Task~Complaint~
        +AddCommentAsync(...) Task~Complaint~
    }
    
    class AgentApiClient {
        -HttpClient _httpClient
        +GetAgentsAsync() Task~List~Agent~~
        +ExecuteAgentAsync(id, inputs) Task~AgentRun~
        +GetRunsAsync(agentId) Task~List~AgentRun~~
    }
    
    class ToastService {
        +ShowSuccess(message) void
        +ShowError(message) void
        +ShowWarning(message) void
    }
    
    class BackgroundActivityService {
        +TrackActivity(...) Task
        +GetActiveActivities() List~Activity~
    }
    
    class IServiceUrlResolver {
        <<interface>>
        +ResolveUrlAsync(serviceName) Task~string~
    }
    
    class ServiceUrlResolver {
        -IConfiguration _configuration
    }
    
    class ErrorHandlingDelegatingHandler {
        +SendAsync() Task~HttpResponseMessage~
    }
    
    IServiceUrlResolver <|.. ServiceUrlResolver
    
    WebFrontend --> DocumentApiClient
    WebFrontend --> LedgerApiClient
    WebFrontend --> MortgageApiClient
    WebFrontend --> CrmApiClient
    WebFrontend --> AgentApiClient
    WebFrontend --> ToastService
    WebFrontend --> BackgroundActivityService
    WebFrontend --> IServiceUrlResolver
    
    DocumentApiClient --> ErrorHandlingDelegatingHandler
    LedgerApiClient --> ErrorHandlingDelegatingHandler
    MortgageApiClient --> ErrorHandlingDelegatingHandler
    CrmApiClient --> ErrorHandlingDelegatingHandler
    AgentApiClient --> ErrorHandlingDelegatingHandler
```

## Data Flow Diagrams

### Mortgage Approval Flow

```mermaid
sequenceDiagram
    participant User
    participant Web as Web Frontend
    participant Mortgage as MortgageApprover
    participant Doc as DocumentService
    participant Ledger as LedgerService
    participant Cosmos as Cosmos DB
    participant AI as Azure OpenAI
    
    User->>Web: Submit Mortgage Application
    Web->>Mortgage: CreateMortgageRequest(userName)
    Mortgage->>Cosmos: Store MortgageRequest
    Mortgage-->>Web: MortgageRequest (Pending)
    
    User->>Web: Upload Documents
    Web->>Doc: UploadDocument(file, owner)
    Doc->>AI: ExtractMetadata(content)
    AI-->>Doc: DocumentMetadata
    Doc->>Cosmos: Store DocumentMeta
    Doc-->>Web: DocumentMeta
    
    User->>Web: Submit Requirement Data
    Web->>Mortgage: SubmitRequirement(data)
    Mortgage->>Cosmos: Update MortgageRequest
    Mortgage-->>Web: Updated MortgageRequest
    
    User->>Web: Request Approval
    Web->>Mortgage: ProcessApproval(requestId)
    
    Mortgage->>Doc: VerifyMortgageDocuments(userName)
    Doc->>Cosmos: Query Documents
    Doc-->>Mortgage: DocumentVerificationResult
    
    Mortgage->>Ledger: CheckSufficientFunds(userName)
    Ledger->>Cosmos: Query Account
    Ledger-->>Mortgage: FundsVerificationResult
    
    Mortgage->>Mortgage: Calculate DTI, Evaluate Criteria
    Mortgage->>Cosmos: Update Status (Approved/Rejected)
    Mortgage-->>Web: Final MortgageRequest
    Web-->>User: Display Result
```

### AI Agent Execution Flow

```mermaid
sequenceDiagram
    participant User
    participant Web as Web Frontend
    participant Agent as AgentService
    participant SK as Semantic Kernel
    participant OpenAI as Azure OpenAI
    participant MCP as MCP Servers
    participant Hub as SignalR Hub
    
    User->>Web: Execute Agent
    Web->>Agent: ExecuteAgent(agentId, inputs)
    Agent->>Agent: Load Agent Config
    Agent->>SK: Create Kernel with Tools
    
    Agent->>MCP: GetTools(serviceUrls)
    MCP-->>Agent: Available Tools
    Agent->>SK: Register MCP Tools
    
    loop Multi-Turn Execution
        Agent->>OpenAI: Send Message + Tools
        OpenAI-->>Agent: Response/Tool Calls
        
        alt Tool Call
            Agent->>Hub: BroadcastLog(toolCall)
            Hub-->>Web: Real-time Update
            Agent->>MCP: InvokeTool(name, args)
            MCP-->>Agent: Tool Result
            Agent->>OpenAI: Continue with Result
        else Final Response
            Agent->>Hub: BroadcastStatus(completed)
            Hub-->>Web: Completion Update
        end
    end
    
    Agent-->>Web: AgentRun (Completed)
    Web-->>User: Display Results
```

## External Dependencies

```mermaid
graph TB
    subgraph NuGet["📦 NuGet Packages"]
        subgraph Aspire["Aspire Integration"]
            AspireCosmos[Aspire.Microsoft.Azure.Cosmos]
            AspireBlobs[Aspire.Azure.Storage.Blobs]
            AspireOpenAI[Aspire.Azure.AI.OpenAI]
            ServiceDiscovery[Microsoft.Extensions.ServiceDiscovery]
            Resilience[Microsoft.Extensions.Http.Resilience]
        end
        
        subgraph AI["AI & ML"]
            AzureOpenAI[Azure.AI.OpenAI]
            SemanticKernel[Microsoft.SemanticKernel]
            SKAzureOpenAI[Microsoft.SemanticKernel.Connectors.AzureOpenAI]
        end
        
        subgraph MCP["Model Context Protocol"]
            MCPCore[ModelContextProtocol]
            MCPAspNet[ModelContextProtocol.AspNetCore]
        end
        
        subgraph Observability["Observability"]
            OTelExporter[OpenTelemetry.Exporter.OpenTelemetryProtocol]
            OTelAspNet[OpenTelemetry.Instrumentation.AspNetCore]
            OTelHttp[OpenTelemetry.Instrumentation.Http]
        end
        
        subgraph Web["Web & API"]
            OpenApi[Microsoft.AspNetCore.OpenApi]
            Scalar[Scalar.AspNetCore]
            SignalR[Microsoft.AspNetCore.SignalR]
        end
    end
    
    subgraph Services["Services"]
        DocSvc[DocumentService]
        LedgerSvc[LedgerService]
        MortgageSvc[MortgageApprover]
        CrmSvc[CrmService]
        AgentSvc[AgentService]
        WebFE[Web Frontend]
    end
    
    DocSvc --> AspireCosmos
    DocSvc --> AspireBlobs
    DocSvc --> AspireOpenAI
    DocSvc --> MCPAspNet
    
    LedgerSvc --> AspireCosmos
    LedgerSvc --> MCPAspNet
    
    MortgageSvc --> AspireCosmos
    MortgageSvc --> MCPAspNet
    
    CrmSvc --> AspireCosmos
    CrmSvc --> MCPAspNet
    
    AgentSvc --> AspireCosmos
    AgentSvc --> AzureOpenAI
    AgentSvc --> SemanticKernel
    AgentSvc --> SKAzureOpenAI
    AgentSvc --> MCPCore
    AgentSvc --> SignalR
    
    WebFE --> SignalR
```

## Cosmos DB Data Model

```mermaid
erDiagram
    HOUZE_DATABASE {
        string id PK
    }
    
    DOCUMENTS_CONTAINER {
        string id PK
        string owner "partition key"
        string fileName
        string contentType
        string blobUrl
        json tags
        json aiMetadata
        datetime createdAt
        datetime updatedAt
    }
    
    LEDGERS_CONTAINER {
        string id PK
        string owner "partition key"
        string entityType "account or transaction"
        decimal balance
        decimal amount
        string description
        datetime timestamp
    }
    
    MORTGAGES_CONTAINER {
        string id PK
        string owner "partition key"
        string requestId
        string status
        json requestData
        json verificationResults
        datetime createdAt
        datetime updatedAt
    }
    
    CRM_CONTAINER {
        string id PK
        string customerName "partition key"
        string title
        string description
        string status
        json comments
        json approvals
        datetime createdAt
    }
    
    AGENTS_CONTAINER {
        string id PK
        string agentId "partition key"
        string entityType "agent or run"
        string name
        string prompt
        json config
        json tools
        json inputs
        json logs
        string output
        datetime createdAt
    }
    
    HOUZE_DATABASE ||--o{ DOCUMENTS_CONTAINER : contains
    HOUZE_DATABASE ||--o{ LEDGERS_CONTAINER : contains
    HOUZE_DATABASE ||--o{ MORTGAGES_CONTAINER : contains
    HOUZE_DATABASE ||--o{ CRM_CONTAINER : contains
    HOUZE_DATABASE ||--o{ AGENTS_CONTAINER : contains
```

## MCP (Model Context Protocol) Architecture

```mermaid
flowchart LR
    subgraph AgentExecution["Agent Execution"]
        SK[Semantic Kernel]
        MCPClient[MCP Client]
    end
    
    subgraph MCPServers["MCP Servers (SSE Transport)"]
        DocMCP["/mcp<br/>DocumentService"]
        LedgerMCP["/mcp<br/>LedgerService"]
        CrmMCP["/mcp<br/>CrmService"]
        MortgageMCP["/mcp<br/>MortgageApprover"]
        AgentMCP["/mcp<br/>AgentService"]
    end
    
    subgraph Tools["Available MCP Tools"]
        DocTools["📄 Document Tools<br/>ListDocuments<br/>GetDocument<br/>VerifyMortgageDocuments"]
        LedgerTools["💰 Ledger Tools<br/>GetAccount<br/>UpdateBalance<br/>CalculateMortgagePayment"]
        CrmTools["👥 CRM Tools<br/>CreateComplaint<br/>GetComplaints<br/>AddComment"]
        MortgageTools["🏠 Mortgage Tools<br/>CreateMortgageRequest<br/>SubmitData<br/>ProcessApproval"]
        AgentTools["🤖 Agent Tools<br/>ListAgents<br/>ExecuteAgent<br/>GetRuns"]
    end
    
    SK --> MCPClient
    MCPClient --> DocMCP
    MCPClient --> LedgerMCP
    MCPClient --> CrmMCP
    MCPClient --> MortgageMCP
    MCPClient --> AgentMCP
    
    DocMCP --> DocTools
    LedgerMCP --> LedgerTools
    CrmMCP --> CrmTools
    MortgageMCP --> MortgageTools
    AgentMCP --> AgentTools
```

## Deployment Architecture

```mermaid
flowchart TB
    subgraph Azure["☁️ Azure"]
        subgraph ACA["Azure Container Apps"]
            WebApp[Web Frontend]
            DocApp[DocumentService]
            LedgerApp[LedgerService]
            MortgageApp[MortgageApprover]
            CrmApp[CrmService]
            AgentApp[AgentService]
        end
        
        subgraph Data["Data Services"]
            CosmosDB[(Azure Cosmos DB)]
            BlobStorage[(Azure Blob Storage)]
        end
        
        subgraph AI["AI Services"]
            OpenAI[Azure OpenAI<br/>gpt-4o, gpt-4o-mini]
        end
        
        subgraph Identity["Identity"]
            ManagedId[Managed Identity]
        end
    end
    
    subgraph Local["🖥️ Local Development"]
        Emulators["Cosmos DB Emulator<br/>Azurite (Blob)"]
        Aspire[".NET Aspire Dashboard"]
    end
    
    WebApp --> DocApp
    WebApp --> LedgerApp
    WebApp --> MortgageApp
    WebApp --> CrmApp
    WebApp --> AgentApp
    
    DocApp --> CosmosDB
    DocApp --> BlobStorage
    DocApp --> OpenAI
    LedgerApp --> CosmosDB
    MortgageApp --> CosmosDB
    CrmApp --> CosmosDB
    AgentApp --> CosmosDB
    AgentApp --> OpenAI
    
    ManagedId -.->|authenticates| OpenAI
    ManagedId -.->|authenticates| CosmosDB
    ManagedId -.->|authenticates| BlobStorage
```

## Key Technologies Summary

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Framework** | .NET 9.0 | Core runtime |
| **Orchestration** | .NET Aspire 9.3.1 | Service orchestration & local dev |
| **Frontend** | Blazor Server | Interactive web UI |
| **Database** | Azure Cosmos DB | Document storage |
| **File Storage** | Azure Blob Storage | Document file storage |
| **AI** | Azure OpenAI (GPT-4o) | Document analysis & agent execution |
| **AI Framework** | Semantic Kernel | LLM orchestration for agents |
| **API Protocol** | Model Context Protocol (MCP) | Tool-based AI integration |
| **Real-time** | SignalR | WebSocket communication |
| **Observability** | OpenTelemetry | Metrics, traces, logging |
| **API Docs** | Scalar + OpenAPI | API documentation |
| **Deployment** | Azure Container Apps | Production hosting |
| **Auth** | DefaultAzureCredential | Keyless authentication |
