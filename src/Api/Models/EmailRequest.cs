using System.ComponentModel.DataAnnotations;

namespace Api.Models;

public class EmailRequest
{
    [Required]
    [MinLength(1)]
    public List<string>? To { get; set; }

    public List<string>? Cc { get; set; }
    public List<string>? Bcc { get; set; }

    [Required]
    public string? Subject { get; set; }

    [Required]
    public string? Body { get; set; }

    public bool IsHtml { get; set; }
}
