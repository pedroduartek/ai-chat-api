using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public class EmailOptions
{
    [Required]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public string? Username { get; set; }

    [Required]
    [EmailAddress]
    public string? From { get; set; }

    [Required]
    [EmailAddress]
    public string? Recipient { get; set; }

    public bool UseSsl { get; set; } = true;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
