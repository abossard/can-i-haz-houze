using System.ComponentModel;

namespace CanIHazHouze.DocumentService;

/// <summary>
/// Represents extracted metadata from a document using AI analysis
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// The document type (e.g., "Invoice", "Contract", "Receipt", "Bank Statement")
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Brief summary of the document content
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// Key entities found in the document (names, companies, amounts, etc.)
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();
    
    /// <summary>
    /// Important dates found in the document
    /// </summary>
    public List<DateExtraction> Dates { get; set; } = new();
    
    /// <summary>
    /// Monetary amounts found in the document
    /// </summary>
    public List<AmountExtraction> Amounts { get; set; } = new();
    
    /// <summary>
    /// Automatically suggested tags based on content
    /// </summary>
    public List<string> SuggestedTags { get; set; } = new();
    
    /// <summary>
    /// Confidence score of the analysis (0.0 to 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// Any warnings or notes about the analysis
    /// </summary>
    public List<string> AnalysisNotes { get; set; } = new();
}

/// <summary>
/// Represents a date extraction from a document
/// </summary>
public class DateExtraction
{
    /// <summary>
    /// The extracted date
    /// </summary>
    public DateTime Date { get; set; }
    
    /// <summary>
    /// The type of date (e.g., "Due Date", "Invoice Date", "Contract Start")
    /// </summary>
    public string DateType { get; set; } = string.Empty;
    
    /// <summary>
    /// Confidence in this date extraction
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Represents a monetary amount extraction from a document
/// </summary>
public class AmountExtraction
{
    /// <summary>
    /// The extracted amount
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// The currency (e.g., "USD", "EUR")
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// The type of amount (e.g., "Total", "Tax", "Subtotal")
    /// </summary>
    public string AmountType { get; set; } = string.Empty;
    
    /// <summary>
    /// Confidence in this amount extraction
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Service interface for AI-powered document analysis
/// </summary>
public interface IDocumentAIService
{
    /// <summary>
    /// Extracts metadata from text content using AI
    /// </summary>
    /// <param name="textContent">The text content to analyze</param>
    /// <param name="fileName">Optional filename for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted metadata</returns>
    Task<DocumentMetadata> ExtractMetadataAsync(
        string textContent, 
        string? fileName = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generates a natural language summary of the document
    /// </summary>
    /// <param name="textContent">The text content to summarize</param>
    /// <param name="maxLength">Maximum length of summary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of the document</returns>
    Task<string> GenerateSummaryAsync(
        string textContent, 
        int maxLength = 200, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Suggests relevant tags for the document based on its content
    /// </summary>
    /// <param name="textContent">The text content to analyze</param>
    /// <param name="existingTags">Any existing tags to consider</param>
    /// <param name="maxTags">Maximum number of tags to suggest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of suggested tags</returns>
    Task<List<string>> SuggestTagsAsync(
        string textContent, 
        List<string>? existingTags = null, 
        int maxTags = 5, 
        CancellationToken cancellationToken = default);
}
