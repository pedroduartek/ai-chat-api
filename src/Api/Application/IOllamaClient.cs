using System.Threading.Tasks;

namespace Api.Application;

public interface IOllamaClient
{
    Task<string> GenerateAsync(string endpoint, object payload);
}
