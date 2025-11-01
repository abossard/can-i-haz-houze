namespace CanIHazHouze.AgentService.Security;

/// <summary>
/// Provides methods to sanitize user input before logging to prevent log forging attacks.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string for safe logging by removing newline characters that could be used for log forging.
    /// </summary>
    /// <param name="value">The value to sanitize</param>
    /// <returns>A sanitized version of the input string safe for logging</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Remove newline characters to prevent log forging
        return value.Replace("\r", "").Replace("\n", " ");
    }
}
