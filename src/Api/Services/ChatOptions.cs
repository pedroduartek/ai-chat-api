namespace Api.Services;

public class ChatOptions
{
    public string ClientName { get; set; } = "ollama";
    public string ChatEndpoint { get; set; } = "/api/chat";
    public string BaseUrl { get; set; } = "http://ollama:11434";
    public string Model { get; set; } = "llama3.2:1b";
    public double Temperature { get; set; } = 0.3;
    public double TopP { get; set; } = 0.9;
    public int TopK { get; set; } = 40;
    public double RepeatPenalty { get; set; } = 1.1;
    public int NumPredict { get; set; } = 256;
    public int NumCtx { get; set; } = 2048;
}
