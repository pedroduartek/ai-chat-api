using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public class ChatOptions
{
    [Required]
    public string ChatEndpoint { get; set; } = "/api/chat";

    [Required]
    public string BaseUrl { get; set; } = "http://ollama:11434";

    [Required]
    public string Model { get; set; } = "llama3.2:1b";

    [Range(0, 2)]
    public double Temperature { get; set; } = 0.3;

    [Range(0, 1)]
    public double TopP { get; set; } = 0.9;

    [Range(1, 500)]
    public int TopK { get; set; } = 40;

    [Range(0.5, 2.0)]
    public double RepeatPenalty { get; set; } = 1.1;

    [Range(1, 8192)]
    public int NumPredict { get; set; } = 256;

    [Range(128, 131072)]
    public int NumCtx { get; set; } = 2048;
}
