namespace Api.Services;

public class ChatOptions
{
    public string ClientName { get; set; } = "ollama";
    public string GenerateEndpoint { get; set; } = "/api/generate";
    public string ModelDefault { get; set; } = "small-llama";
    public string PromptPrefix { get; set; } = "Reply to the question shortly:";
    public string BaseUrl { get; set; } = "http://ollama:11434";
}
