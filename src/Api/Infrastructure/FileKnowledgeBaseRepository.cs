using System.Text.Json;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure;

using Api.Application;

public class FileKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly ILogger<FileKnowledgeBaseRepository> _logger;

    // Entry IDs that contain meta-instructions rather than factual content.
    // These are already covered by the system prompt and should not be injected into the KB block.
    private static readonly HashSet<string> _excludedIds = new(StringComparer.OrdinalIgnoreCase) { "usage" };

    public FileKnowledgeBaseRepository(ILogger<FileKnowledgeBaseRepository> logger)
    {
        _logger = logger;
    }

    public Task<string> GetKnowledgeBaseAsync()
    {
        var entries = LoadEntries();
        return Task.FromResult(FormatEntries(entries));
    }

    public Task<string> GetRelevantKnowledgeBaseAsync(string query)
    {
        var entries = LoadEntries();
        if (entries.Count == 0)
            return Task.FromResult(string.Empty);

        var queryTokens = ExpandWithSynonyms(Tokenize(query));
        var matched = entries
            .Where(e => EntrySearchTokens(e).Any(k => queryTokens.Contains(k)))
            .ToList();

        // Return empty when nothing matches - model will use fallback response.
        // Returning all entries causes hallucinations with irrelevant context.
        _logger.LogInformation(
            "KB lookup: query tokens [{Tokens}] matched {Matched}/{Total} entries",
            string.Join(", ", queryTokens), matched.Count, entries.Count);

        return Task.FromResult(FormatEntries(matched));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private sealed record KbEntry(string Id, string Title, string Text, IReadOnlyList<string> Keywords);

    private List<KbEntry> LoadEntries()
    {
        try
        {
            var kbPath = Path.Combine(AppContext.BaseDirectory, "Resources", "website_kb.txt");
            if (!File.Exists(kbPath))
                return [];

            var entries = new List<KbEntry>();
            foreach (var line in File.ReadAllLines(kbPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        continue;

                    var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    if (_excludedIds.Contains(id))
                        continue;

                    var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
                    var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;

                    var keywords = new List<string>();
                    if (root.TryGetProperty("keywords", out var kwProp) && kwProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var kw in kwProp.EnumerateArray())
                        {
                            var kwStr = kw.GetString();
                            if (!string.IsNullOrWhiteSpace(kwStr))
                                keywords.Add(kwStr.ToLowerInvariant());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                        entries.Add(new KbEntry(id, title, text, keywords));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping malformed KB line: {Line}", line);
                }
            }
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge base from disk");
            return [];
        }
    }

    private static string FormatEntries(IEnumerable<KbEntry> entries)
    {
        var parts = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .Select(e => string.IsNullOrWhiteSpace(e.Title)
                ? e.Text
                : $"[{e.Title}] {e.Text}");
        return string.Join("\n\n", parts);
    }

    private static readonly Dictionary<string, string[]> _synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hobby"]          = ["hobbies", "interest", "interests", "passion", "passions"],
        ["hobbies"]        = ["hobby", "interest", "interests", "passion"],
        ["interest"]       = ["hobbies", "hobby", "passion"],
        ["interests"]      = ["hobbies", "hobby"],
        ["passion"]        = ["hobbies", "hobby", "interests"],
        ["passions"]       = ["hobbies", "hobby"],
        ["achievement"]    = ["achievements", "accomplishment", "accomplishments"],
        ["achievements"]   = ["achievement", "accomplishment", "accomplishments"],
        ["accomplishment"] = ["achievements", "achievement"],
        ["accomplish"]     = ["achievements", "achievement"],
        ["resume"]         = ["cv", "curriculum"],
        ["curriculum"]     = ["cv", "resume"],
        ["degree"]         = ["education", "university", "school"],
        ["university"]     = ["education", "degree", "school", "polytechnic"],
        ["school"]         = ["education", "degree", "university"],
        ["college"]        = ["education", "degree", "university"],
        ["studied"]        = ["education"],
        ["study"]          = ["education"],
        ["project"]        = ["projects"],
        ["projects"]       = ["project"],
        ["company"]        = ["companies", "employer", "experience"],
        ["companies"]      = ["company", "employer"],
        ["employer"]       = ["companies", "company", "experience"],
        ["live"]           = ["location", "lisbon"],
        ["lives"]          = ["location", "lisbon"],
        ["from"]           = ["location"],
        ["contact"]        = ["email", "linkedin", "github", "reach"],
        ["reach"]          = ["contact", "email"],
        ["hire"]           = ["contact", "email", "linkedin"],
        ["philosophy"]     = ["values", "beliefs", "mindset", "approach"],
        ["values"]         = ["philosophy", "beliefs"],
        ["believes"]       = ["philosophy", "values"],
        ["opinion"]        = ["philosophy", "values"],
    };

    private static HashSet<string> ExpandWithSynonyms(HashSet<string> tokens)
    {
        var expanded = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
            if (_synonyms.TryGetValue(token, out var synonyms))
                foreach (var s in synonyms)
                    expanded.Add(s);
        return expanded;
    }

    private static HashSet<string> Tokenize(string text)
    {
        // Lower-case words of 3+ chars; strips punctuation.
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"\b[a-zA-Z#\+]{3,}\b"))
            tokens.Add(m.Value.ToLowerInvariant());
        return tokens;
    }

    // Returns all searchable tokens for an entry: its explicit keywords plus
    // tokens derived from the id and title so natural-language queries like
    // "companies" still hit "EXPERIENCE_ENHESA" via the word "experience".
    private static IEnumerable<string> EntrySearchTokens(KbEntry entry)
    {
        foreach (var kw in entry.Keywords)
            yield return kw;
        foreach (var t in Tokenize(entry.Id))
            yield return t;
        foreach (var t in Tokenize(entry.Title))
            yield return t;
    }
}
