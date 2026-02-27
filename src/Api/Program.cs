using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Api.Services;

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
var envModel = config["OLLAMA_MODEL"];
if (!string.IsNullOrEmpty(envModel))
    chatOptsPre.ModelDefault = envModel;
var clientName = config["Chat:ClientName"] ?? chatOptsPre.ClientName;
var baseUrl = config["OLLAMA_BASE_URL"] ?? chatOptsPre.BaseUrl;

builder.Services.Configure<ChatOptions>(options =>
{
    chatSection.Bind(options);
    var env = config["OLLAMA_MODEL"];
    if (!string.IsNullOrEmpty(env))
        options.ModelDefault = env;
});

builder.Services.AddHttpClient(clientName, c =>
{
    c.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Enable CORS for the configured frontend origins
app.UseCors("AllowFrontend");

app.MapControllers();

app.Run();
