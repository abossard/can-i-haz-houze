using System.ComponentModel;
using ModelContextProtocol.Server;
using CanIHazHouze.DocumentService;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace CanIHazHouze.DocumentService.McpTools;

[McpServerToolType]
public class DocumentTools
{
    private readonly IDocumentService _documentService;
    private readonly IDocumentAIService _aiService;

    public DocumentTools(IDocumentService documentService, IDocumentAIService aiService)
    {
        _documentService = documentService;
        _aiService = aiService;
    }

    [McpServerTool]
    [Description("Get all documents for a user")]
    public async Task<IEnumerable<DocumentMeta>> ListDocuments(
        [Description("Document owner username")] string owner)
    {
        return await _documentService.GetDocumentsAsync(owner);
    }

    [McpServerTool]
    [Description("Get document metadata by ID")]
    public async Task<DocumentMeta?> GetDocument(
        [Description("Document ID")] string id,
        [Description("Document owner username")] string owner)
    {
        return await _documentService.GetDocumentAsync(Guid.Parse(id), owner);
    }

    [McpServerTool]
    [Description("Update document tags")]
    public async Task<DocumentMeta?> UpdateDocumentTags(
        [Description("Document ID")] string id,
        [Description("Document owner username")] string owner,
        [Description("New list of tags")] string[] tags)
    {
        return await _documentService.UpdateDocumentTagsAsync(Guid.Parse(id), owner, tags.ToList());
    }

    [McpServerTool]
    [Description("Delete a document")]
    public async Task<bool> DeleteDocument(
        [Description("Document ID")] string id,
        [Description("Document owner username")] string owner)
    {
        return await _documentService.DeleteDocumentAsync(Guid.Parse(id), owner);
    }

    [McpServerTool]
    [Description("Verify that a user has uploaded all required mortgage documents")]
    public async Task<DocumentVerificationResult> VerifyMortgageDocuments(
        [Description("Document owner username")] string owner)
    {
        var documents = await _documentService.GetDocumentsAsync(owner);
        
        var hasIncomeDocuments = documents.Any(d => d.Tags.Any(t => 
            t.Contains("income", StringComparison.OrdinalIgnoreCase) || 
            t.Contains("salary", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("pay", StringComparison.OrdinalIgnoreCase)));
            
        var hasCreditReport = documents.Any(d => d.Tags.Any(t => 
            t.Contains("credit", StringComparison.OrdinalIgnoreCase)));
            
        var hasEmploymentVerification = documents.Any(d => d.Tags.Any(t => 
            t.Contains("employment", StringComparison.OrdinalIgnoreCase)));
            
        var hasPropertyAppraisal = documents.Any(d => d.Tags.Any(t => 
            t.Contains("appraisal", StringComparison.OrdinalIgnoreCase)));

        return new DocumentVerificationResult
        {
            UserName = owner,
            HasIncomeDocuments = hasIncomeDocuments,
            HasCreditReport = hasCreditReport,
            HasEmploymentVerification = hasEmploymentVerification,
            HasPropertyAppraisal = hasPropertyAppraisal,
            AllDocumentsVerified = hasIncomeDocuments && hasCreditReport && hasEmploymentVerification && hasPropertyAppraisal,
            TotalDocuments = documents.Count()
        };
    }

    [McpServerTool]
    [Description("Analyze a document using AI to extract metadata and insights")]
    public async Task<DocumentAnalysisResult> AnalyzeDocumentAI(
        [Description("Document ID to analyze")] string documentId,
        [Description("Document owner")] string owner)
    {
        var docMeta = await _documentService.GetDocumentAsync(Guid.Parse(documentId), owner);
        if (docMeta == null)
            return new DocumentAnalysisResult { Error = "Document not found" };

        // Get document content stream
        var contentStream = await _documentService.GetDocumentContentAsync(Guid.Parse(documentId), owner);
        if (contentStream == null)
            return new DocumentAnalysisResult { Error = "Document content not available" };

        // For simplicity, reading text content (in real scenario, might need PDF parsing)
        using var reader = new StreamReader(contentStream);
        var textContent = await reader.ReadToEndAsync();

        // AI analysis
        var metadata = await _aiService.ExtractMetadataAsync(textContent, docMeta.FileName);
        
        return new DocumentAnalysisResult
        {
            Metadata = metadata,
            Success = true
        };
    }
}

// Custom result types for MCP tool outputs
public class DocumentVerificationResult
{
    public string UserName { get; set; } = string.Empty;
    public bool HasIncomeDocuments { get; set; }
    public bool HasCreditReport { get; set; }
    public bool HasEmploymentVerification { get; set; }
    public bool HasPropertyAppraisal { get; set; }
    public bool AllDocumentsVerified { get; set; }
    public int TotalDocuments { get; set; }
}

public class DocumentAnalysisResult
{
    public bool Success { get; set; }
    public DocumentMetadata? Metadata { get; set; }
    public string? Error { get; set; }
}
