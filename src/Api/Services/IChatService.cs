using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Services;

public interface IChatService
{
    Task<string> GenerateAnswerAsync(string message);
    IAsyncEnumerable<string> StreamAnswerAsync(string message, CancellationToken cancellationToken = default);
}
