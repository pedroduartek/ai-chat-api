using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Api.Application;

namespace Api.Services;

public class LlmKeepWarmService : BackgroundService
{
    private readonly ILogger<LlmKeepWarmService> _logger;
    private readonly IServiceProvider _provider;
    private readonly WarmupOptions _options;
    private readonly ILastActivityTracker _tracker;
    private readonly ChatOptions _chatOptions;
    private readonly object _pingLock = new();

    public LlmKeepWarmService(ILogger<LlmKeepWarmService> logger, IServiceProvider provider, IOptions<WarmupOptions> options, ILastActivityTracker tracker, IOptions<ChatOptions> chatOptions)
    {
        _logger = logger;
        _provider = provider;
        _options = options?.Value ?? new WarmupOptions();
        _tracker = tracker;
        _chatOptions = chatOptions?.Value ?? new ChatOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("LlmKeepWarmService disabled via configuration.");
            return;
        }

        _logger.LogInformation("LlmKeepWarmService started, interval {Minutes}m", _options.IntervalMinutes);

        var delay = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var last = _tracker.GetLastActivityUtc();
            var since = DateTime.UtcNow - last;
            if (since < delay)
            {
                _logger.LogDebug("Skipping warmup ping; last activity {Elapsed}s ago (< {Interval}s)", (int)since.TotalSeconds, (int)delay.TotalSeconds);
                continue;
            }

            // Prevent overlapping pings
            lock (_pingLock)
            {
                // create a scope and run the warmup call
                try
                {
                    using var scope = _provider.CreateScope();
                    var client = (IOllamaClient)scope.ServiceProvider.GetService(typeof(IOllamaClient));
                    if (client is null)
                    {
                        _logger.LogWarning("IOllamaClient not available for warmup.");
                        continue;
                    }

                    var payload = new
                    {
                        model = _chatOptions.Model,
                        messages = new[]
                        {
                            new { role = "system", content = "Warmup ping" },
                            new { role = "user", content = _options.Prompt }
                        },
                        stream = false,
                        options = new { num_predict = _options.MaxTokens }
                    };

                    // Execute warmup and log similarly to normal chat requests (question, answer, duration)
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var content = client.GenerateAsync(_chatOptions.ChatEndpoint, payload).GetAwaiter().GetResult();
                    sw.Stop();

                    var answer = string.IsNullOrEmpty(content) ? string.Empty : (content.Length > 200 ? content.Substring(0, 200) + "..." : content);
                    _logger.LogInformation("Warmup Q={Question} A={Answer} Duration={Duration}ms Endpoint={Endpoint}", _options.Prompt, answer, sw.ElapsedMilliseconds, _chatOptions.ChatEndpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Warmup ping failed");
                }
            }
        }

        _logger.LogInformation("LlmKeepWarmService stopping");
    }
}
