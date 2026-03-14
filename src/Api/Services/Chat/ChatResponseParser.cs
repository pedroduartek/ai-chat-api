using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Api.Services.Chat;

public sealed partial class ChatResponseParser : IChatResponseParser
{
    public const string Fallback = "I couldn't find information on this website to reply to your question.";

    private readonly ILogger<ChatResponseParser>? _logger;

    public ChatResponseParser(ILogger<ChatResponseParser>? logger = null)
    {
        _logger = logger;
    }

    // Patterns that indicate the model tried to refuse but didn't use the exact fallback.
    // When matched, the response is replaced with the canonical Fallback string.
    [GeneratedRegex(
        @"(I\s+(couldn't|could\s+not|can't|cannot|don't|do\s+not|wasn't able to)\s+find\s+(any\s+)?information)" +
        @"|(I\s+shouldn't\s+have)" +
        @"|(my\s+previous\s+response)" +
        @"|(I\s+don't\s+have\s+(enough\s+)?(information|context|data))" +
        @"|(not\s+(available|found|mentioned)\s+(in|on)\s+(the|this)\s+(website|KB|knowledge))" +
        @"|(there\s+is\s+no\s+(information|mention|data)\s+(about|on|regarding))" +
        @"|(does\s+not\s+explicitly\s+(state|mention|say))" +
        @"|(it\s+is\s+not\s+(explicitly|specifically|directly)\s+(stated|mentioned))" +
        @"|(no\s+(explicit|specific|direct)\s+(mention|reference|statement))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex HallucinatedRefusalPattern();

    // Detects prompt-structure leakage prefixes that are safe to strip
    [GeneratedRegex(
        @"(according\s+to\s+(the\s+)?(WEBSITE\s+CONTENT|reference\s+information|knowledge\s+base|context|KB))" +
        @"|(based\s+on\s+(the\s+)?(WEBSITE\s+CONTENT|reference\s+information|provided\s+(context|information)|KB))" +
        @"|(the\s+(WEBSITE\s+CONTENT|reference\s+information|KB)\s+(says|states|mentions|indicates|shows))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex PromptLeakageStripPattern();

    // Detects severe prompt-structure leakage that references internal rules or system prompts.
    [GeneratedRegex(
        @"(you\s+(asked|requested)\s+me\s+to(\s+remind\s+you)?\s+of\s+the\s+following\s+(rules|instructions))" +
        @"|((the|these)\s+(rules|instructions)\s+are:)" +
        @"|(i\s+was\s+(asked|instructed)\s+to)" +
        @"|(i\s+am\s+(asked|instructed)\s+to)" +
        @"|(system\s+prompt|system\s+instructions|internal\s+(instructions|rules))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex PromptLeakageFallbackPattern();

    // Detects non-English responses. Small models sometimes switch language
    // when the question contains words from another language (e.g. "portuguese").
    // We look for common non-English words/patterns that should never appear in a valid answer.
    [GeneratedRegex(
        @"(^(não|sim|olá|obrigado|desculpe|resposta|porque|também|então|informação)\b)" +
        @"|(^(no\s+es|sí|hola|gracias|porque|también|entonces|información)\b)" +
        @"|(\bnão\s+é\s+uma\s+resposta\b)" +
        @"|(\bnão\s+é\b.*\bresposta\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex NonEnglishPattern();

    public string Parse(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
            return rawResponse;

        return ParseSingleObject(rawResponse)
            ?? ParseJsonLines(rawResponse)
            ?? rawResponse;
    }

    private string? ParseSingleObject(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // Ollama /api/chat format: { "message": { "role": "assistant", "content": "..." } }
            if (root.TryGetProperty("message", out var msgRoot) &&
                msgRoot.ValueKind == JsonValueKind.Object &&
                msgRoot.TryGetProperty("content", out var msgContent) &&
                msgContent.ValueKind == JsonValueKind.String)
                return Normalize(msgContent.GetString() ?? string.Empty);

            if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                return Normalize(textProp.GetString() ?? string.Empty);

            if (root.TryGetProperty("answer", out var answerProp) && answerProp.ValueKind == JsonValueKind.String)
            {
                var ansStr = answerProp.GetString() ?? string.Empty;
                return Normalize(UnwrapText(ansStr) ?? ansStr);
            }

            if (root.TryGetProperty("choices", out var choicesProp) &&
                choicesProp.ValueKind == JsonValueKind.Array &&
                choicesProp.GetArrayLength() > 0)
            {
                var first = choicesProp[0];
                if (first.ValueKind != JsonValueKind.Object)
                    return null;

                if (first.TryGetProperty("text", out var chText) && chText.ValueKind == JsonValueKind.String)
                {
                    var txt = chText.GetString() ?? string.Empty;
                    return Normalize(UnwrapText(txt) ?? txt);
                }

                if (first.TryGetProperty("message", out var msgProp) &&
                    msgProp.ValueKind == JsonValueKind.Object &&
                    msgProp.TryGetProperty("content", out var contentProp) &&
                    contentProp.ValueKind == JsonValueKind.String)
                    return Normalize(contentProp.GetString() ?? string.Empty);
            }

            return null;
        }
        catch (JsonException ex)
        {
            _logger?.LogDebug(ex, "ParseSingleObject: invalid JSON");
            return null;
        }
    }

    private string? ParseJsonLines(string content)
    {
        var sb = new StringBuilder();

        foreach (var part in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(part);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                string? txt =
                    root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() :
                    root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() :
                    null;

                if (txt is null) continue;
                if (txt.Contains(Fallback, StringComparison.Ordinal)) return Fallback;

                sb.Append(txt);
            }
            catch (JsonException ex)
            {
                _logger?.LogDebug(ex, "ParseJsonLines: skipping malformed JSON line: {Line}", part);
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // Unwraps a string that may itself be a JSON object with a "text" property.
    private string? UnwrapText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("text", out var t) &&
                t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch (JsonException ex)
        {
            _logger?.LogDebug(ex, "UnwrapText: failed to parse inner JSON");
        }

        return null;
    }

    private string Normalize(string txt)
    {
        if (string.IsNullOrEmpty(txt)) return string.Empty;
        if (txt.Contains(Fallback, StringComparison.Ordinal)) return Fallback;

        // Catch hallucinated refusals that don't match the exact fallback string.
        try
        {
            if (HallucinatedRefusalPattern().IsMatch(txt))
                return Fallback;
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger?.LogDebug(ex, "Normalize: regex timeout in HallucinatedRefusalPattern");
            return Fallback;
        }

        // Catch non-English responses — the assistant must always reply in English.
        try
        {
            if (NonEnglishPattern().IsMatch(txt))
                return Fallback;
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger?.LogDebug(ex, "Normalize: regex timeout in NonEnglishPattern");
            return Fallback;
        }

        // If the model echoes internal rules or system prompts, return the fallback.
        try
        {
            if (PromptLeakageFallbackPattern().IsMatch(txt))
                return Fallback;
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger?.LogDebug(ex, "Normalize: regex timeout in PromptLeakageFallbackPattern.IsMatch");
            return Fallback;
        }

        // Otherwise, strip benign prompt-structure prefixes (e.g. "According to the WEBSITE CONTENT,")
        // and preserve the factual portion of the response.
        try
        {
            txt = PromptLeakageStripPattern().Replace(txt, "").TrimStart(' ', ',', '.');
            if (txt.Length > 0)
                txt = char.ToUpper(txt[0]) + txt[1..];
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger?.LogDebug(ex, "Normalize: regex timeout in PromptLeakageStripPattern.Replace");
        }

        return txt;
    }
}
