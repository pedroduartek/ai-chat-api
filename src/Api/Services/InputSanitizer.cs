using System.Text.RegularExpressions;

namespace Api.Services;

/// <summary>
/// Detects and neutralises known prompt-injection patterns in user messages
/// before they reach the LLM.  This is not a silver bullet — defence-in-depth
/// (system-prompt separation, model guardrails, output filtering) is still needed —
/// but it raises the bar for trivial injection attempts.
/// </summary>
public static partial class InputSanitizer
{
    // Patterns that attempt to override the system prompt or impersonate the system role.
    // Compiled once via source-generated regex for zero-allocation matching.
    [GeneratedRegex(
        @"(ignore\s+(all\s+)?(previous|prior|above|earlier)\s+(instructions?|prompts?|rules?|context))" +
        @"|(forget\s+(everything|all|your)\s*(instructions?|rules?|prompts?)?)" +
        @"|(you\s+are\s+now\b)" +
        @"|(system\s*:\s*)" +
        @"|(act\s+as\s+(if\s+you\s+are|a|an)\b)" +
        @"|(pretend\s+(you\s+are|to\s+be)\b)" +
        @"|(new\s+instructions?\s*:)" +
        @"|(override\s+(previous|system|all)\b)" +
        @"|(do\s+not\s+follow\s+(your|the|any)\s+(rules?|instructions?|prompts?))" +
        @"|(disregard\s+(all|any|previous|your)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex InjectionPattern();

    /// <summary>
    /// Returns <c>true</c> when the message contains patterns commonly used
    /// in prompt-injection attacks.
    /// </summary>
    public static bool ContainsInjection(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        try
        {
            return InjectionPattern().IsMatch(message);
        }
        catch (RegexMatchTimeoutException)
        {
            // If the regex times out on adversarial input, treat it as suspicious.
            return true;
        }
    }

    /// <summary>
    /// Strips detected injection fragments from the message, returning a cleaned version.
    /// Callers should still check <see cref="ContainsInjection"/> first and may choose to
    /// reject the request outright instead of sanitising.
    /// </summary>
    public static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        try
        {
            return InjectionPattern().Replace(message, "[REDACTED]").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }
}
