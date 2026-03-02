using System.Text.Json;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Api.Infrastructure;

using Api.Application;

public class FileKnowledgeBaseRepository : IKnowledgeBaseRepository
{
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
                catch
                {
                    // ignore malformed lines
                }
            }

            var kb = parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
            return Task.FromResult(kb);
        }
        catch
        {
            return Task.FromResult(string.Empty);
        }
    }
}
