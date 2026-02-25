using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient("ollama", c =>
{
    var baseUrl = builder.Configuration["OLLAMA_BASE_URL"] ?? "http://ollama:11434";
    c.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/chat", async (ChatRequest req, IHttpClientFactory clientFactory, IConfiguration config) =>
{
    var messageText = req?.Message;
    if (string.IsNullOrWhiteSpace(messageText))
    {
        return Results.BadRequest(new { error = "message required" });
    }

    var client = clientFactory.CreateClient("ollama");
    var model = config["OLLAMA_MODEL"] ?? "small-llama";
    var promptToSend = "Reply to the question shortly:" + "\n\n" + messageText;
    var payload = new { model, prompt = promptToSend };
    var resp = await client.PostAsJsonAsync("/api/generate", payload);
    var content = await resp.Content.ReadAsStringAsync();

    string finalAnswer = content;
    try
    {
        var parts = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        var parsedAny = false;

        foreach (var part in parts)
        {
            try
            {
                using var doc = JsonDocument.Parse(part);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("response", out var responseProp) && responseProp.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(responseProp.GetString());
                        parsedAny = true;
                    }
                    else if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(textProp.GetString());
                        parsedAny = true;
                    }
                }
            }
            catch
            {
            }
        }

        if (parsedAny)
            finalAnswer = sb.ToString();
    }
    catch
    {
    }

    return Results.Json(new { answer = finalAnswer });
});

app.Run();

record ChatRequest
{
    public string? Prompt { get; init; }
    public string? Message { get; init; }
}
