using System.Text.Json;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure;

using Api.Application;

public class FileKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly ILogger<FileKnowledgeBaseRepository> _logger;

    public FileKnowledgeBaseRepository(ILogger<FileKnowledgeBaseRepository> logger)
    {
        _logger = logger;
    }
    public Task<string> GetKnowledgeBaseAsync()
    {
        try
        {
            var kbPath = Path.Combine(AppContext.BaseDirectory, "Resources", "website_kb.txt");
            if (!File.Exists(kbPath))
                return Task.FromResult(string.Empty);

            var lines = File.ReadAllLines(kbPath);
            var parts = new List<string>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(textProp.GetString() ?? string.Empty);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping malformed KB line: {Line}", line);
                }
            }

            var kb = parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
            return Task.FromResult(kb);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge base from disk");
            return Task.FromResult(string.Empty);
        }
    }
}
