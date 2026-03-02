using System.Threading.Tasks;

namespace Api.Application;

public interface IKnowledgeBaseRepository
{
    Task<string> GetKnowledgeBaseAsync();
}
