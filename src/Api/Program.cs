using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Api.Services;
using Api.Application;
using Api.Infrastructure;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
// Configure CORS to allow the frontend origin for browser requests
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
});
ThreadPool.GetMinThreads(out var workerMin, out var compMin);
var desiredWorker = Math.Max(workerMin, processorCount * 2);
ThreadPool.SetMinThreads(desiredWorker, compMin);

builder.Services.AddScoped<IKnowledgeBaseRepository, FileKnowledgeBaseRepository>();
builder.Services.AddScoped<IOllamaClient, OllamaHttpClient>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Enable CORS for the configured frontend origins
app.UseCors("AllowFrontend");

app.MapControllers();

app.Run();
