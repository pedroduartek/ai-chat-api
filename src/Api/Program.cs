using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Api.Application;
using Api.Infrastructure;
using Api.Options;
using Api.Security;
using Api.Services.Chat;
using Api.Services.Email;
using Api.Services.Turnstile;
using Api.Services.Warmup;
using System.Threading;

using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;
using Polly;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());

// Surface Serilog sink errors (e.g. Loki connectivity issues) in the console.
SelfLog.Enable(Console.Error);

var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ai-chat-api");

var lokiUrl = builder.Configuration["GRAFANA_LOKI_URL"];
var lokiUser = builder.Configuration["GRAFANA_LOKI_USER"];
var lokiApiKey = builder.Configuration["GRAFANA_LOKI_API_KEY"];

if (!string.IsNullOrEmpty(lokiUrl) && !string.IsNullOrEmpty(lokiUser) && !string.IsNullOrEmpty(lokiApiKey))
{
    loggerConfig = loggerConfig.WriteTo.GrafanaLoki(
        lokiUrl,
        labels: new[]
        {
            new LokiLabel { Key = "app", Value = "ai-chat-api" },
            new LokiLabel { Key = "env", Value = builder.Environment.EnvironmentName }
        },
        credentials: new LokiCredentials
        {
            Login = lokiUser,
            Password = lokiApiKey
        },
        textFormatter: new RenderedCompactJsonFormatter()
    );
}

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Trust the reverse proxy for the public request scheme so logs reflect HTTPS.
    // Client IP continues to come from CF-Connecting-IP / direct connection only.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    var rlSection = builder.Configuration.GetSection("RateLimiting");
    var globalTokens = rlSection.GetValue<int?>("TokensPerPeriod") ?? 60;
    var globalPeriodSeconds = rlSection.GetValue<int?>("ReplenishmentPeriodSeconds") ?? 60;

    var chatSection = rlSection.GetSection("Chat");
    var chatTokens = chatSection.GetValue<int?>("TokensPerPeriod") ?? 12;
    var chatPeriodSeconds = chatSection.GetValue<int?>("ReplenishmentPeriodSeconds") ?? 60;

    var chatStreamSection = rlSection.GetSection("ChatStream");
    var chatStreamPermitLimit = chatStreamSection.GetValue<int?>("PermitLimit") ?? 4;
    var chatStreamWindowSeconds = chatStreamSection.GetValue<int?>("WindowSeconds") ?? 60;

    var emailSection = rlSection.GetSection("Email");
    var emailPermitLimit = emailSection.GetValue<int?>("PermitLimit") ?? 3;
    var emailWindowSeconds = emailSection.GetValue<int?>("WindowSeconds") ?? 3600;

    var healthSection = rlSection.GetSection("Health");
    var healthPermitLimit = healthSection.GetValue<int?>("PermitLimit") ?? 30;
    var healthWindowSeconds = healthSection.GetValue<int?>("WindowSeconds") ?? 60;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        return CreateTokenBucketPartition(httpContext, globalTokens, globalPeriodSeconds);
    });

    options.AddPolicy(RateLimitPolicyNames.Chat, httpContext =>
        CreateTokenBucketPartition(httpContext, chatTokens, chatPeriodSeconds));

    options.AddPolicy(RateLimitPolicyNames.ChatStream, httpContext =>
        CreateFixedWindowPartition(httpContext, chatStreamPermitLimit, chatStreamWindowSeconds));

    options.AddPolicy(RateLimitPolicyNames.Email, httpContext =>
        CreateFixedWindowPartition(httpContext, emailPermitLimit, emailWindowSeconds));

    options.AddPolicy(RateLimitPolicyNames.Health, httpContext =>
        CreateFixedWindowPartition(httpContext, healthPermitLimit, healthWindowSeconds));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api.RateLimiting");
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["ClientIp"] = GetClientIp(context.HttpContext.Request),
            ["RequestPath"] = context.HttpContext.Request.Path.Value ?? string.Empty,
            ["RequestMethod"] = context.HttpContext.Request.Method
        }))
        {
            logger.LogWarning("Request rejected by rate limiter");
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please slow down and try again later." },
            cancellationToken: ct);
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "AllowFrontend",
        policy =>
        {
            policy.WithOrigins("https://pedroduartek.com", "https://www.pedroduartek.com")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});
var config = builder.Configuration;
var chatSection = config.GetSection("Chat");

builder.Services
    .AddOptions<ChatOptions>()
    .Bind(chatSection)
    .ValidateDataAnnotations()
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Chat:BaseUrl must be an absolute URI.")
    .ValidateOnStart();

builder.Services.PostConfigure<ChatOptions>(options =>
{
    options.BaseUrl = config["OLLAMA_BASE_URL"] ?? options.BaseUrl;
    options.Model = config["OLLAMA_MODEL"] ?? options.Model;
});

builder.Services
    .AddOptions<EmailOptions>()
    .Bind(config.GetSection("Email"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<WarmupOptions>()
    .Bind(config.GetSection("Warmup"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ResilienceOptions>()
    .Bind(config.GetSection("Resilience"))
    .ValidateDataAnnotations()
    .Validate(options => options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds,
        "Resilience:AttemptTimeoutSeconds must be lower than Resilience:TotalTimeoutSeconds, otherwise a retry can never fit inside the budget.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TurnstileOptions>()
    .Bind(config.GetSection("Turnstile"))
    .ValidateDataAnnotations()
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.SecretKey), "Turnstile:SecretKey is required when Turnstile is enabled.")
    .Validate(options => !options.Enabled || Uri.TryCreate(options.SiteVerifyUrl, UriKind.Absolute, out _), "Turnstile:SiteVerifyUrl must be an absolute URI.")
    .ValidateOnStart();

var processorCount = Environment.ProcessorCount;
var maxConns = Math.Max(4, processorCount * 4);
builder.Services.AddHttpClient<IChatCompletionClient, OllamaHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = maxConns
})
// Resilience pipeline in front of the LLM. Outer to inner:
// total timeout -> retry -> circuit breaker -> per-attempt timeout.
// Only transient failures are retried (never a 4xx, which is deterministic, and
// never a streaming request), so one visitor message cannot fan out into several
// full generations on a single self-hosted model.
.AddResilienceHandler("ollama", (pipeline, context) =>
{
    var resilience = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

    pipeline.AddTimeout(new HttpTimeoutStrategyOptions
    {
        Name = "ollama-total",
        Timeout = TimeSpan.FromSeconds(resilience.TotalTimeoutSeconds)
    });

    if (resilience.MaxRetryAttempts > 0)
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            Name = "ollama-retry",
            MaxRetryAttempts = resilience.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(resilience.RetryBaseDelayMilliseconds),
            UseJitter = true,
            ShouldHandle = args =>
            {
                // Streaming opts out entirely: replaying it re-runs the generation.
                if (ChatRequestKinds.IsStreamingRequest(args.Outcome.Result?.RequestMessage
                        ?? args.Context.GetRequestMessage()))
                {
                    return ValueTask.FromResult(false);
                }

                // Transient only: network faults, 5xx, 408 and 429. A 400/404 from
                // Ollama (bad options, model not pulled) will never fix itself.
                return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome));
            }
        });
    }

    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        Name = "ollama-breaker",
        FailureRatio = resilience.CircuitBreakerFailureRatio,
        SamplingDuration = TimeSpan.FromSeconds(resilience.CircuitBreakerSamplingSeconds),
        MinimumThroughput = resilience.CircuitBreakerMinimumThroughput,
        BreakDuration = TimeSpan.FromSeconds(resilience.CircuitBreakerDurationSeconds)
    });

    pipeline.AddTimeout(new HttpTimeoutStrategyOptions
    {
        Name = "ollama-attempt",
        Timeout = TimeSpan.FromSeconds(resilience.AttemptTimeoutSeconds)
    });
});
ThreadPool.GetMinThreads(out var workerMin, out var compMin);
var desiredWorker = Math.Max(workerMin, processorCount * 2);
ThreadPool.SetMinThreads(desiredWorker, compMin);

// Limit request body size to 1 MB to prevent oversized payloads before model binding.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1_048_576);

builder.Services.AddSingleton<IKnowledgeBaseRepository, FileKnowledgeBaseRepository>();
builder.Services.AddSingleton<ChatMessageValidator>();
builder.Services.AddSingleton<IChatResponseParser, ChatResponseParser>();
builder.Services.AddScoped<IChatRequestFactory, ChatRequestFactory>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddHttpClient<ITurnstileVerificationService, TurnstileVerificationService>();
// Keep-alive / warming services
builder.Services.AddSingleton<ILastActivityTracker, LastActivityTracker>();
builder.Services.AddHostedService<LlmKeepWarmService>();

var app = builder.Build();

app.UseForwardedHeaders();

// Swagger exposure is configuration-driven so production can opt in without code changes.
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, _, exception) =>
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;

        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
            return LogEventLevel.Warning;

        if (httpContext.Request.Path.StartsWithSegments("/health"))
            return LogEventLevel.Debug;

        return LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("ClientIp", GetClientIp(httpContext.Request));
        diagnosticContext.Set("EndpointName", httpContext.GetEndpoint()?.DisplayName ?? "unknown");
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrWhiteSpace(userAgent))
            diagnosticContext.Set("UserAgent", userAgent);
    };
});

// Ensure CORS runs before any middleware that can short-circuit requests
// (e.g. rate limiting) so preflight/OPTIONS responses still include
// the Access-Control-Allow-* headers.
app.UseCors("AllowFrontend");
app.UseRateLimiter();

// Security headers middleware — defense-in-depth alongside Caddy-level headers.
app.Use(async (context, next) =>
{
    var isSwaggerRequest = context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = isSwaggerRequest
        ? "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'"
        : "default-src 'none'";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Global exception handler — returns structured JSON errors instead of raw exceptions.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred. Please try again later.\"}");
    });
});

// Tell crawlers not to index the API host.
app.MapGet("/", () =>
{
    if (swaggerEnabled)
        return Results.Ok(new { service = "ai-chat-api", status = "ok", health = "/health", docs = "/swagger" });

    return Results.Ok(new { service = "ai-chat-api", status = "ok", health = "/health" });
})
    .ExcludeFromDescription();

// Tell crawlers not to index the API host.
app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /\n", "text/plain"))
    .ExcludeFromDescription();

app.MapControllers();

static RateLimitPartition<string> CreateTokenBucketPartition(HttpContext httpContext, int tokenLimit, int periodSeconds)
{
    if (HttpMethods.IsOptions(httpContext.Request.Method))
        return RateLimitPartition.GetNoLimiter("cors-preflight");

    return RateLimitPartition.GetTokenBucketLimiter(GetClientIp(httpContext.Request), _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = tokenLimit,
        TokensPerPeriod = tokenLimit,
        ReplenishmentPeriod = TimeSpan.FromSeconds(periodSeconds),
        AutoReplenishment = true,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0
    });
}

static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext httpContext, int permitLimit, int windowSeconds)
{
    if (HttpMethods.IsOptions(httpContext.Request.Method))
        return RateLimitPartition.GetNoLimiter("cors-preflight");

    return RateLimitPartition.GetFixedWindowLimiter(GetClientIp(httpContext.Request), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromSeconds(windowSeconds),
        AutoReplenishment = true,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0
    });
}

static string GetClientIp(HttpRequest request)
{
    // When behind Cloudflare, CF-Connecting-IP is the ONLY trustworthy client-IP header.
    // Cloudflare always overwrites it with the true visitor IP and strips spoofed values.
    // We intentionally do NOT fall back to X-Forwarded-For / X-Real-IP because those can
    // be trivially spoofed by an attacker to obtain a fresh rate-limit bucket.
    if (request.Headers.TryGetValue("CF-Connecting-IP", out var cf) && !StringValues.IsNullOrEmpty(cf))
        return cf.ToString();

    // Fallback: use the direct connection IP (correct when Cloudflare is not in the path,
    // e.g. during local development or direct VPS access).
    return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

app.Run();

public partial class Program;
