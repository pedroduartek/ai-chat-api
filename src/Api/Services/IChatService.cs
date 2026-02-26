using System.Threading.Tasks;

namespace Api.Services;

public interface IChatService
{
    Task<string> GenerateAnswerAsync(string message);
}
