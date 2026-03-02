using System.Text;
using System.Text.Json;

namespace Api.Services;

public sealed class ChatResponseParser : IChatResponseParser
{
    public const string Fallback = "I couldn't find information to reply to your question.";

    public string Parse(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
            return rawResponse;

        return ParseSingleObject(rawResponse)
            ?? ParseJsonLines(rawResponse)
            ?? rawResponse;
    }

    private static string? ParseSingleObject(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

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
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ParseJsonLines(string content)
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
            catch (JsonException) { }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // Unwraps a string that may itself be a JSON object with a "text" property.
    private static string? UnwrapText(string json)
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
        catch (JsonException) { }
        return null;
    }

    private static string Normalize(string txt)
    {
        if (string.IsNullOrEmpty(txt)) return string.Empty;
        if (txt.Contains(Fallback, StringComparison.Ordinal)) return Fallback;
        return txt;
    }
}
