# Azure OpenAI Integration Setup Guide

## Overview

This guide shows how to add Azure OpenAI integration to your .NET Aspire mortgage application for document processing and metadata extraction.

## What's Been Added

### 1. AppHost Configuration (`AppHost.cs`)

- Added Azure OpenAI resource with automatic deployment/connection string handling
- Configured GPT-4o-mini model deployment for production
- Added OpenAI reference to the Document Service

### 2. Document Service Enhancements

**New NuGet Package:**
- `Aspire.Azure.AI.OpenAI` for OpenAI client integration

**New Services:**
- `IDocumentAIService` - Interface for AI document analysis
- `DocumentAIService` - Implementation using Azure OpenAI

**New API Endpoints:**
- `POST /documents/{id}/analyze` - AI analysis of uploaded documents
- `POST /documents/suggest-tags` - AI-powered tag suggestions

### 3. AI Capabilities

**Document Analysis Features:**
- Document type classification (Invoice, Contract, Receipt, etc.)
- Automatic summary generation
- Entity extraction (names, companies, amounts, dates)
- Tag suggestions based on content
- Confidence scoring for analysis quality

## Setup Instructions

### 1. Local Development Setup

For local development, you need an existing Azure OpenAI resource. Add this to your user secrets:

```json
{
  "ConnectionStrings": {
    "openai": "Endpoint=https://your-openai-resource.openai.azure.com/;Key=your-api-key;"
  }
}
```

To set user secrets for the AppHost:

```bash
cd src/CanIHazHouze.AppHost
dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://your-openai-resource.openai.azure.com/;Key=your-api-key;"
```

### 2. Azure Production Setup

When deploying to Azure with `azd up`, the OpenAI resource will be automatically provisioned with:
- A new Azure OpenAI account
- GPT-4o-mini model deployment
- Automatic connection configuration

### 3. Model Configuration

The Document Service supports configurable model deployment names via configuration:

```json
{
  "OpenAI": {
    "ModelDeployment": "gpt-4o-mini"
  }
}
```

## Usage Examples

### 1. Analyze an Uploaded Document

```bash
# First upload a document
curl -X POST "https://localhost:7501/documents" \
  -H "Content-Type: multipart/form-data" \
  -F "owner=john_doe" \
  -F "file=@invoice.txt" \
  -F "tags=invoice,2024"

# Then analyze it (using the returned document ID)
curl -X POST "https://localhost:7501/documents/{document-id}/analyze?owner=john_doe"
```

**Response Example:**
```json
{
  "documentId": "123e4567-e89b-12d3-a456-426614174000",
  "fileName": "invoice.txt",
  "originalTags": ["invoice", "2024"],
  "aiAnalysis": {
    "documentType": "Invoice",
    "summary": "Invoice from ABC Corp for services rendered in December 2024",
    "entities": {
      "company": "ABC Corp",
      "amount": "$1,250.00",
      "dueDate": "2024-12-31"
    },
    "suggestedTags": ["invoice", "abc-corp", "december", "payment-due"],
    "confidenceScore": 0.95
  },
  "analyzedAt": "2024-06-17T10:30:00Z"
}
```

### 2. Get AI Tag Suggestions

```bash
curl -X POST "https://localhost:7501/documents/suggest-tags" \
  -H "Content-Type: application/json" \
  -d '{
    "textContent": "Monthly rent payment receipt for 123 Main St, December 2024",
    "maxTags": 5
  }'
```

**Response Example:**
```json
{
  "suggestedTags": ["rent", "receipt", "housing", "december", "payment"],
  "requestedMaxTags": 5,
  "analyzedAt": "2024-06-17T10:30:00Z"
}
```

## Supported File Types

### Currently Supported (Full Text Analysis)
- `.txt` - Plain text files
- `.md` - Markdown files

### Partially Supported (Metadata Analysis)
- All other file types use filename and existing metadata for analysis
- Future enhancement: Add OCR for images and PDF text extraction

## Configuration Options

### Document Service (`appsettings.json`)

```json
{
  "OpenAI": {
    "ModelDeployment": "gpt-4o-mini"
  },
  "DocumentStorage": {
    "BaseDirectory": "./UserDocs"
  }
}
```

### AppHost Environment Variables

For production customization:

```bash
# Set custom model deployment name
azd env set OPENAI_MODEL_DEPLOYMENT "gpt-4o"

# Deploy to Azure
azd up
```

## Security Considerations

1. **API Keys**: Never hardcode API keys - use Azure Key Vault or user secrets
2. **Access Control**: Document analysis respects owner-based access control
3. **Data Privacy**: Document content is sent to Azure OpenAI for analysis
4. **Rate Limiting**: Consider implementing rate limiting for AI endpoints in production

## Cost Optimization

1. **Model Selection**: 
   - `gpt-4o-mini` - Lower cost, good for most document analysis
   - `gpt-4o` - Higher accuracy, higher cost for complex documents

2. **Token Management**:
   - Document analysis is limited to 2000 output tokens
   - Summary generation limited to 150 tokens
   - Tag suggestions limited to 100 tokens

3. **Caching**: Consider implementing response caching for repeated analysis

## Monitoring and Logging

The service includes comprehensive logging:
- AI analysis requests and responses
- Error handling and fallback responses
- Performance metrics and confidence scores

Monitor costs and usage through:
- Azure OpenAI resource metrics
- Application Insights integration (via .NET Aspire)
- Custom logging in the DocumentAIService

## Future Enhancements

1. **OCR Integration**: Add text extraction from images and PDFs
2. **Document Validation**: Use AI to validate document authenticity
3. **Smart Categorization**: Automatic document filing based on AI analysis
4. **Multi-language Support**: Analysis in multiple languages
5. **Batch Processing**: Analyze multiple documents simultaneously

## Troubleshooting

### Common Issues

1. **"OpenAI connection failed"**
   - Check connection string configuration
   - Verify Azure OpenAI resource is accessible
   - Check API key permissions

2. **"Model deployment not found"**
   - Verify model deployment name in configuration
   - Check Azure OpenAI resource has the required model deployed

3. **"Analysis timeout"**
   - Check network connectivity
   - Consider reducing document size for analysis
   - Verify Azure OpenAI quota limits

### Debug Mode

Enable detailed logging by setting log level to `Debug`:

```json
{
  "Logging": {
    "LogLevel": {
      "CanIHazHouze.DocumentService.DocumentAIService": "Debug"
    }
  }
}
```
