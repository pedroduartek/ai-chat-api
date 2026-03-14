using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public class TurnstileOptions
{
    public bool Enabled { get; set; } = true;

    [Url]
    public string SiteVerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public string? SecretKey { get; set; }

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 10;

    public string[] AllowedHostnames { get; set; } = [];
}
