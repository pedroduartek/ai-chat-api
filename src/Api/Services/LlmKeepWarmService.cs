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
                // create a scope and run the warmup call via the full chat pipeline
                try
                {
                    using var scope = _provider.CreateScope();
                    var chatService = (IChatService)scope.ServiceProvider.GetService(typeof(IChatService));
                    if (chatService is null)
                    {
                        _logger.LogWarning("IChatService not available for warmup.");
                        continue;
                    }

                    var warmupQuestion = "What skills does Pedro have?";
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var content = chatService.GenerateAnswerAsync(warmupQuestion).GetAwaiter().GetResult();
                    sw.Stop();

                    var answer = string.IsNullOrEmpty(content) ? string.Empty : (content.Length > 200 ? content.Substring(0, 200) + "..." : content);
                    _logger.LogInformation("Warmup Q={Question} A={Answer} Duration={Duration}ms Source={Source}", warmupQuestion, answer, sw.ElapsedMilliseconds, "warmup");
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
