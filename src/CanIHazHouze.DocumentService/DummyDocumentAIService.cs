using System.Text.RegularExpressions;

namespace CanIHazHouze.DocumentService;

/// <summary>
/// Dummy fallback implementation used when Azure OpenAI endpoint is not configured.
/// Provides deterministic, lightweight results so tests and local dev without secrets pass.
/// </summary>
public class DummyDocumentAIService : IDocumentAIService
{
    private static readonly List<string> GenericTags = ["document", "mortgage", "finance", "user", "uploaded"];    

    public Task<DocumentMetadata> ExtractMetadataAsync(string textContent, string? fileName = null, CancellationToken cancellationToken = default)
    {
        // Very naive extractions: find dates and dollar amounts via regex
        var metadata = new DocumentMetadata
        {
            DocumentType = InferType(textContent, fileName),
            Summary = CreateSummary(textContent),
            ConfidenceScore = 0.0, // Explicitly low to indicate dummy
            SuggestedTags = SuggestTagsInternal(textContent, null, 5),
            AnalysisNotes = ["Dummy AI service used - configure ConnectionStrings:openai for real analysis"]
        };

        // Date extraction
        var dateMatches = Regex.Matches(textContent, "\\b(20[0-9]{2}|19[0-9]{2})[-/.](0?[1-9]|1[0-2])[-/.](0?[1-9]|[12][0-9]|3[01])\\b");
        foreach (Match m in dateMatches.Take(3))
        {
            if (DateTime.TryParse(m.Value, out var d))
            {
                metadata.Dates.Add(new DateExtraction { Date = d, DateType = "DetectedDate", Confidence = 0.1 });
            }
        }

        // Amount extraction
        var amountMatches = Regex.Matches(textContent, "\\$\\s?([0-9]+(\\.[0-9]{2})?)");
        foreach (Match m in amountMatches.Take(3))
        {
            if (decimal.TryParse(m.Groups[1].Value, out var amt))
            {
                metadata.Amounts.Add(new AmountExtraction { Amount = amt, Currency = "USD", AmountType = "DetectedAmount", Confidence = 0.1 });
            }
        }

        return Task.FromResult(metadata);
    }

    public Task<string> GenerateSummaryAsync(string textContent, int maxLength = 200, CancellationToken cancellationToken = default)
    {
        var summary = CreateSummary(textContent);
        return Task.FromResult(summary.Length > maxLength ? summary.Substring(0, maxLength) : summary);
    }

    public Task<List<string>> SuggestTagsAsync(string textContent, List<string>? existingTags = null, int maxTags = 5, CancellationToken cancellationToken = default)
    {
        var tags = SuggestTagsInternal(textContent, existingTags, maxTags);
        return Task.FromResult(tags);
    }

    private static string InferType(string content, string? fileName)
    {
        if (fileName != null && fileName.Contains("invoice", StringComparison.OrdinalIgnoreCase)) return "Invoice";
        if (content.Contains("loan", StringComparison.OrdinalIgnoreCase)) return "Loan";
        if (content.Contains("contract", StringComparison.OrdinalIgnoreCase)) return "Contract";
        return "Document";
    }

    private static string CreateSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Empty document";
        var trimmed = content.Trim().Replace('\n', ' ');
        return trimmed.Length <= 180 ? trimmed : trimmed.Substring(0, 180) + "...";
    }

    private static List<string> SuggestTagsInternal(string textContent, List<string>? existing, int max)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(textContent))
        {
            if (textContent.Contains("income", StringComparison.OrdinalIgnoreCase)) tags.Add("income");
            if (textContent.Contains("credit", StringComparison.OrdinalIgnoreCase)) tags.Add("credit");
            if (textContent.Contains("employment", StringComparison.OrdinalIgnoreCase)) tags.Add("employment");
            if (textContent.Contains("appraisal", StringComparison.OrdinalIgnoreCase)) tags.Add("appraisal");
            if (textContent.Contains("mortgage", StringComparison.OrdinalIgnoreCase)) tags.Add("mortgage");
        }
        // Add generic fallbacks
        foreach (var g in GenericTags)
        {
            if (!tags.Contains(g)) tags.Add(g);
        }
        if (existing != null)
        {
            foreach (var e in existing)
            {
                if (!tags.Contains(e, StringComparer.OrdinalIgnoreCase)) tags.Add(e);
            }
        }
        return tags.Take(max).ToList();
    }
}
