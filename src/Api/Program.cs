using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;

using Api.Services;
using Api.Application;
using Api.Infrastructure;
using System.Threading;

using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.Grafana.Loki;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Surface Serilog sink errors (e.g. Loki connectivity issues) in the console.
SelfLog.Enable(Console.Error);

var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());

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
        textFormatter: new Serilog.Formatting.Json.JsonFormatter()
    );
}

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    var rlSection = builder.Configuration.GetSection("RateLimiting");
    var tokens = rlSection.GetValue<int?>("TokensPerPeriod") ?? 100;
    var periodSeconds = rlSection.GetValue<int?>("ReplenishmentPeriodSeconds") ?? 60;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var req = httpContext.Request;
        var clientId = GetClientIdentifier(req);
        return RateLimitPartition.GetTokenBucketLimiter(clientId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokens,
            TokensPerPeriod = tokens,
            ReplenishmentPeriod = TimeSpan.FromSeconds(periodSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many requests", ct);
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
var chatOptsPre = chatSection.Get<ChatOptions>() ?? new ChatOptions();
var clientName = config["Chat:ClientName"] ?? chatOptsPre.ClientName;
var baseUrl = config["OLLAMA_BASE_URL"] ?? chatOptsPre.BaseUrl;

builder.Services.Configure<ChatOptions>(options =>
{
    chatSection.Bind(options);
});

var processorCount = Environment.ProcessorCount;
var maxConns = Math.Max(4, processorCount * 4);
builder.Services.AddHttpClient(clientName, c =>
{
    c.BaseAddress = new Uri(baseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = maxConns
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => !msg.IsSuccessStatusCode)
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
ThreadPool.GetMinThreads(out var workerMin, out var compMin);
var desiredWorker = Math.Max(workerMin, processorCount * 2);
ThreadPool.SetMinThreads(desiredWorker, compMin);

// Limit request body size to 1 MB to prevent oversized payloads before model binding.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1_048_576);

builder.Services.AddSingleton<IKnowledgeBaseRepository, FileKnowledgeBaseRepository>();
builder.Services.AddScoped<IOllamaClient, OllamaHttpClient>();
builder.Services.AddSingleton<IChatResponseParser, ChatResponseParser>();
builder.Services.AddScoped<IChatService, ChatService>();
var emailSection = builder.Configuration.GetSection("Email");
builder.Services.Configure<Api.Services.EmailOptions>(emailSection);
builder.Services.AddScoped<Api.Services.IEmailService, Api.Services.SmtpEmailService>();
// Keep-alive / warming services
builder.Services.AddSingleton<Api.Services.ILastActivityTracker, Api.Services.LastActivityTracker>();
builder.Services.Configure<Api.Services.WarmupOptions>(builder.Configuration.GetSection("Warmup"));
builder.Services.AddHostedService<Api.Services.LlmKeepWarmService>();

var app = builder.Build();

// Only expose Swagger in development — leaking the API schema in production is an information-disclosure risk.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Ensure CORS runs before any middleware that can short-circuit requests
// (e.g. rate limiting) so preflight/OPTIONS responses still include
// the Access-Control-Allow-* headers.
app.UseCors("AllowFrontend");
app.UseRateLimiter();

// Security headers middleware — defense-in-depth alongside Caddy-level headers.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
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

app.MapControllers();

static string GetClientIdentifier(HttpRequest request)
{
    if (request.Headers.TryGetValue("x-api-key", out var apiKey) && !StringValues.IsNullOrEmpty(apiKey))
        return $"apiKey:{apiKey.ToString()}";

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
