using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public class WarmupOptions
{
    public bool Enabled { get; set; } = true;

    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 2;
}
