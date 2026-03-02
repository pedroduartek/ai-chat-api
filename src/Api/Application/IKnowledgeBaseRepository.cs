using System.Threading.Tasks;

namespace Api.Application;

public interface IKnowledgeBaseRepository
{
    Task<string> GetKnowledgeBaseAsync();

    /// <summary>
    /// Returns only the KB entries whose keywords overlap with words in <paramref name="query"/>.
    /// Falls back to all entries when no keyword matches are found.
    /// </summary>
    Task<string> GetRelevantKnowledgeBaseAsync(string query);
}
