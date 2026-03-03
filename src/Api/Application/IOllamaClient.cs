using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Application;

public interface IOllamaClient
{
    Task<string> GenerateAsync(string endpoint, object payload);
    IAsyncEnumerable<string> StreamAsync(string endpoint, object payload, CancellationToken cancellationToken = default);
}
