# Document Workflow Guide

This guide is the canonical explanation of how document handling works in Can I Haz Houze.

## 1) What the document system does

The DocumentService handles:
- document upload and storage,
- metadata persistence,
- AI analysis and tag suggestion,
- mortgage document completeness checks,
- download/update/delete operations.

## 2) Core components

| Component | Role |
| --- | --- |
| `CanIHazHouze.DocumentService` | Main API + MCP surface for documents |
| Azure Blob Storage / Azurite | Binary file storage |
| Azure Cosmos DB | Document metadata and queryable records |
| Azure OpenAI (`gpt-4o-mini`) | Analysis, summarization, tag suggestions |
| MortgageApprover | Uses document verification to evaluate applications |
| Web UI (`/documents`, `/api-docs`) | Human-friendly interaction and API discovery |

## 3) End-to-end flow

1. **Upload**
   - User uploads via UI, REST API, or MCP tool.
   - Service validates owner + file metadata and receives file bytes.

2. **Store file**
   - Binary content is written to Blob Storage container.
   - Naming follows `{documentId}_{fileName}`.

3. **Store metadata**
   - Cosmos DB record stores owner, tags, content type, size, and blob reference.

4. **Analyze (optional but recommended)**
   - Azure OpenAI extracts document type, summary, entities, and suggested tags.
   - Analysis results are attached to document metadata.

5. **Verify for mortgage workflows**
   - Verification checks required categories (income, credit, employment, property).
   - MortgageApprover uses this status as part of approval evaluation.

6. **Lifecycle operations**
   - List, fetch, retag, download, and delete are available.
   - Delete removes metadata and file content.

## 4) API quick reference (DocumentService)

| Endpoint | Purpose |
| --- | --- |
| `POST /documents` | Upload a document |
| `GET /documents?owner={owner}` | List owner documents |
| `GET /documents/{id}?owner={owner}` | Get one document metadata record |
| `PUT /documents/{id}/tags?owner={owner}` | Replace tags |
| `POST /documents/{id}/analyze?owner={owner}` | Run AI analysis |
| `POST /documents/suggest-tags` | Suggest tags from text content |
| `GET /documents/{id}/download?owner={owner}` | Download original file |
| `DELETE /documents/{id}?owner={owner}` | Delete document |

## 5) MCP tools quick reference (DocumentService)

| Tool | Purpose |
| --- | --- |
| `upload_document` | Upload (base64 content) |
| `list_documents` | List owner documents |
| `get_document` | Retrieve a document by ID |
| `update_document_tags` | Update tag set |
| `delete_document` | Delete document |
| `verify_mortgage_documents` | Check required mortgage docs |
| `analyze_document_ai` | Trigger AI analysis |

Use `GET /mcp/capabilities` on the service to inspect live tool contracts.

## 6) Local setup requirements

### Required auth model
- Keyless auth only (`DefaultAzureCredential`)
- Run `az login`
- Ensure role assignment: `Cognitive Services OpenAI User` (or higher)

### Required configuration
Set the same OpenAI endpoint user secret in:
- `src/CanIHazHouze.AppHost`
- `src/CanIHazHouze.AgentService`
- `src/CanIHazHouze.DocumentService`
- `src/CanIHazHouze.Tests`

Example:

```bash
OPENAI_ENDPOINT="Endpoint=https://your-resource.openai.azure.com/"
for p in CanIHazHouze.AppHost CanIHazHouze.AgentService CanIHazHouze.DocumentService CanIHazHouze.Tests; do
  (cd "src/$p" && dotnet user-secrets set "ConnectionStrings:openai" "$OPENAI_ENDPOINT")
done
```

## 7) Verify locally in 5 minutes

1. Start app:
   ```bash
   cd src
   dotnet run --project CanIHazHouze.AppHost
   ```
2. Open the web app and upload a sample file.
3. Trigger analysis from UI or call `POST /documents/{id}/analyze`.
4. Confirm tags/metadata are present.
5. Download the file and verify original filename/content.

## 8) Production notes

- Use managed identity / Entra auth for OpenAI and storage access.
- Keep owner-scoped access checks on all document operations.
- Monitor OpenAI usage + token costs.
- Apply rate limits on AI-heavy endpoints for predictable spend.

## 9) Where to go deeper

- Service docs: [src/CanIHazHouze.MortgageApprover/README.md](src/CanIHazHouze.MortgageApprover/README.md)
- MCP usage details: [src/MCP_USAGE_GUIDE.md](src/MCP_USAGE_GUIDE.md)
- Production hardening: [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md)
