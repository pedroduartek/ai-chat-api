using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddHttpClient("ollama", c =>
{
    var baseUrl = builder.Configuration["OLLAMA_BASE_URL"] ?? "http://ollama:11434";
    c.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
