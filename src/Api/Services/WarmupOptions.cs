namespace Api.Services;

public class WarmupOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public string Prompt { get; set; } = "ping";
    public int MaxTokens { get; set; } = 8;
}
