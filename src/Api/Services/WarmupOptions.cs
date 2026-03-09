namespace Api.Services;

public class WarmupOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 2;
}
