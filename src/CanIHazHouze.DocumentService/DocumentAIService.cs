using Azure.AI.Inference;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanIHazHouze.DocumentService;

/// <summary>
/// AI-powered document analysis service using Azure AI Foundry
/// </summary>
public class DocumentAIService : IDocumentAIService
{
    private readonly ChatCompletionsClient _chatClient;
    private readonly ILogger<DocumentAIService> _logger;

    public DocumentAIService(
        ChatCompletionsClient chatClient, 
        ILogger<DocumentAIService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DocumentMetadata> ExtractMetadataAsync(
        string textContent, 
        string? fileName = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting metadata from document {FileName}", fileName ?? "unknown");
            
            var systemPrompt = CreateMetadataExtractionPrompt();
            var userPrompt = CreateDocumentAnalysisPrompt(textContent, fileName);

            var requestOptions = new ChatCompletionsOptions
            {
                Messages =
                {
                    new ChatRequestSystemMessage(systemPrompt),
                    new ChatRequestUserMessage(userPrompt)
                },
                Temperature = 0.1f, // Low temperature for consistent extraction
                MaxTokens = 2000
            };

            var response = await _chatClient.CompleteAsync(requestOptions, cancellationToken);

            var jsonResponse = response.Value.Content;
            _logger.LogDebug("AI response: {Response}", jsonResponse);

            var metadata = ParseMetadataResponse(jsonResponse);
            
            _logger.LogInformation("Successfully extracted metadata for document {FileName}", fileName ?? "unknown");
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata from document {FileName}", fileName ?? "unknown");
            
            // Return a basic metadata object with error information
            return new DocumentMetadata
            {
                DocumentType = "Unknown",
                Summary = "Analysis failed due to an error",
                AnalysisNotes = [$"Error during analysis: {ex.Message}"],
                ConfidenceScore = 0.0
            };
        }
    }

    public async Task<string> GenerateSummaryAsync(
        string textContent, 
        int maxLength = 200, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating summary for document content");
            
            var prompt = $"""
                Please provide a concise summary of the following document content in {maxLength} characters or less.
                Focus on the key points, purpose, and important details.
                
                Document content:
                {textContent}
                """;

            var requestOptions = new ChatCompletionsOptions
            {
                Messages = { new ChatRequestUserMessage(prompt) },
                Temperature = 0.3f,
                MaxTokens = 150
            };

            var response = await _chatClient.CompleteAsync(requestOptions, cancellationToken);

            var summary = response.Value.Content.Trim();
            
            _logger.LogInformation("Successfully generated summary");
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary");
            return "Summary generation failed";
        }
    }

    public async Task<List<string>> SuggestTagsAsync(
        string textContent, 
        List<string>? existingTags = null, 
        int maxTags = 5, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Suggesting tags for document content");
            
            var existingTagsText = existingTags?.Any() == true 
                ? $"Existing tags: {string.Join(", ", existingTags)}\n" 
                : "";
            
            var prompt = $"""
                Based on the following document content, suggest {maxTags} relevant tags for organization and categorization.
                {existingTagsText}
                Return only the tags as a JSON array of strings, nothing else.
                Focus on document type, subject matter, and key themes.
                
                Document content:
                {textContent}
                """;

            var requestOptions = new ChatCompletionsOptions
            {
                Messages = { new ChatRequestUserMessage(prompt) },
                Temperature = 0.4f,
                MaxTokens = 100
            };

            var response = await _chatClient.CompleteAsync(requestOptions, cancellationToken);

            var jsonResponse = response.Value.Content;
            
            // Try to parse as JSON array
            try
            {
                var tags = JsonSerializer.Deserialize<List<string>>(jsonResponse) ?? new List<string>();
                _logger.LogInformation("Successfully suggested {TagCount} tags", tags.Count);
                return tags;
            }
            catch (JsonException)
            {
                // Fallback: try to extract tags from the response
                _logger.LogWarning("Failed to parse tags JSON, using fallback extraction");
                return ExtractTagsFromText(jsonResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting tags");
            return new List<string> { "document", "unprocessed" };
        }
    }

    private string CreateMetadataExtractionPrompt()
    {
        return """
            You are an expert document analysis system. Analyze the provided document content and extract structured metadata.
            
            Return your analysis as a JSON object with the following structure:
            {
                "documentType": "string - type of document (e.g., Invoice, Contract, Receipt, Report, Letter)",
                "summary": "string - brief 1-2 sentence summary of the document",
                "entities": {
                    "key": "value pairs of important entities (names, companies, addresses, etc.)"
                },
                "dates": [
                    {
                        "date": "ISO date string",
                        "dateType": "string - type of date (e.g., Due Date, Invoice Date)",
                        "confidence": "number between 0 and 1"
                    }
                ],
                "amounts": [
                    {
                        "amount": "number - monetary amount",
                        "currency": "string - currency code (default USD)",
                        "amountType": "string - type of amount (e.g., Total, Tax, Subtotal)",
                        "confidence": "number between 0 and 1"
                    }
                ],
                "suggestedTags": ["array of suggested tags"],
                "confidenceScore": "number between 0 and 1 - overall confidence in analysis",
                "analysisNotes": ["array of any warnings or notes about the analysis"]
            }
            
            Be thorough but concise. Focus on extracting the most important and useful information.
            """;
    }

    private string CreateDocumentAnalysisPrompt(string textContent, string? fileName)
    {
        var fileInfo = !string.IsNullOrEmpty(fileName) ? $"Filename: {fileName}\n\n" : "";
        
        return $"""
            {fileInfo}Document content to analyze:
            
            {textContent}
            
            Please analyze this document and extract the metadata as requested.
            """;
    }

    private DocumentMetadata ParseMetadataResponse(string jsonResponse)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonResponse, options);
            
            var metadata = new DocumentMetadata
            {
                DocumentType = GetStringProperty(parsed, "documentType") ?? "Unknown",
                Summary = GetStringProperty(parsed, "summary") ?? "",
                Entities = GetDictionaryProperty(parsed, "entities") ?? new Dictionary<string, string>(),
                Dates = GetDateExtractions(parsed, "dates"),
                Amounts = GetAmountExtractions(parsed, "amounts"),
                SuggestedTags = GetStringArrayProperty(parsed, "suggestedTags") ?? new List<string>(),
                ConfidenceScore = GetDoubleProperty(parsed, "confidenceScore") ?? 0.0,
                AnalysisNotes = GetStringArrayProperty(parsed, "analysisNotes") ?? new List<string>()
            };

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing metadata response: {Response}", jsonResponse);
            
            return new DocumentMetadata
            {
                DocumentType = "Unknown",
                Summary = "Failed to parse AI response",
                AnalysisNotes = { "Error parsing AI response" },
                ConfidenceScore = 0.0
            };
        }
    }

    private List<string> ExtractTagsFromText(string text)
    {
        // Simple fallback tag extraction
        var tags = new List<string>();
        
        // Look for common document-related words
        var commonTags = new[] { "document", "financial", "legal", "business", "personal", "invoice", "contract", "receipt" };
        
        foreach (var tag in commonTags)
        {
            if (text.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }
        
        return tags.Take(5).ToList();
    }

    // Helper methods for JSON parsing
    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String 
            ? prop.GetString() 
            : null;
    }

    private double? GetDoubleProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number 
            ? prop.GetDouble() 
            : null;
    }

    private Dictionary<string, string>? GetDictionaryProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, string>();
        foreach (var item in prop.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                dict[item.Name] = item.Value.GetString() ?? "";
            }
        }
        return dict;
    }

    private List<string>? GetStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString() ?? "");
            }
        }
        return list;
    }

    private List<DateExtraction> GetDateExtractions(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return new List<DateExtraction>();

        var dates = new List<DateExtraction>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var dateStr = GetStringProperty(item, "date");
                if (DateTime.TryParse(dateStr, out var date))
                {
                    dates.Add(new DateExtraction
                    {
                        Date = date,
                        DateType = GetStringProperty(item, "dateType") ?? "",
                        Confidence = GetDoubleProperty(item, "confidence") ?? 0.0
                    });
                }
            }
        }
        return dates;
    }

    private List<AmountExtraction> GetAmountExtractions(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return new List<AmountExtraction>();

        var amounts = new List<AmountExtraction>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var amount = GetDoubleProperty(item, "amount");
                if (amount.HasValue)
                {
                    amounts.Add(new AmountExtraction
                    {
                        Amount = (decimal)amount.Value,
                        Currency = GetStringProperty(item, "currency") ?? "USD",
                        AmountType = GetStringProperty(item, "amountType") ?? "",
                        Confidence = GetDoubleProperty(item, "confidence") ?? 0.0
                    });
                }
            }
        }
        return amounts;
    }
}
