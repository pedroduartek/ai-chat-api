namespace Api.Services;

public class EmailOptions
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? Username { get; set; }
    public string? From { get; set; }
    public bool UseSsl { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
}
