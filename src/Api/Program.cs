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
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
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

builder.Services.AddSingleton<IKnowledgeBaseRepository, FileKnowledgeBaseRepository>();
builder.Services.AddScoped<IOllamaClient, OllamaHttpClient>();
builder.Services.AddSingleton<IChatResponseParser, ChatResponseParser>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseRateLimiter();
app.UseCors("AllowFrontend");

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

    // CF-Connecting-IP is set by Cloudflare; X-Real-IP / X-Forwarded-For by Caddy/nginx.
    if (request.Headers.TryGetValue("CF-Connecting-IP", out var cf) && !StringValues.IsNullOrEmpty(cf))
        return cf.ToString();

    if (request.Headers.TryGetValue("X-Real-IP", out var xr) && !StringValues.IsNullOrEmpty(xr))
        return xr.ToString();

    if (request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !StringValues.IsNullOrEmpty(xff))
    {
        var first = xff.ToString().Split(',')[0].Trim();
        if (!string.IsNullOrEmpty(first)) return first;
    }

    return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

app.Run();
