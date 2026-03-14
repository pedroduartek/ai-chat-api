using System;
using System.Threading;
using System.Threading.Tasks;
using Api.Options;
using Api.Services.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Services.Warmup;

public class LlmKeepWarmService : BackgroundService
{
    private readonly ILogger<LlmKeepWarmService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WarmupOptions _options;
    private readonly ILastActivityTracker _tracker;
    private readonly object _pingLock = new();
    private volatile bool _isPinging = false;

    public LlmKeepWarmService(ILogger<LlmKeepWarmService> logger, IServiceScopeFactory scopeFactory, IOptions<WarmupOptions> options, ILastActivityTracker tracker)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options?.Value ?? new WarmupOptions();
        _tracker = tracker;
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

            // Prevent overlapping pings using a simple in-memory flag.
            lock (_pingLock)
            {
                if (_isPinging)
                {
                    _logger.LogDebug("Skipping warmup; another warmup is running");
                    continue;
                }
                _isPinging = true;
            }

            try
            {
                // create a scope and run the warmup call via the full chat pipeline
                using var scope = _scopeFactory.CreateScope();
                var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

                var warmupQuestion = "What skills does Pedro have?";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string content = string.Empty;
                try
                {
                    content = await chatService.GenerateAnswerAsync(warmupQuestion, stoppingToken);
                    sw.Stop();

                    var answer = string.IsNullOrEmpty(content) ? string.Empty : (content.Length > 200 ? content[..200] + "..." : content);
                    _logger.LogInformation("Warmup Q={Question} A={Answer} Duration={Duration}ms Source={Source}", warmupQuestion, answer, sw.ElapsedMilliseconds, "warmup");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Warmup ping failed");
                }
            }
            finally
            {
                lock (_pingLock)
                {
                    _isPinging = false;
                }
            }
        }

        _logger.LogInformation("LlmKeepWarmService stopping");
    }
}
