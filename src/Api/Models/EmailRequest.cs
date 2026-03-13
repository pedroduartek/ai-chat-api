using System.ComponentModel.DataAnnotations;

namespace Api.Models;

public class EmailRequest
{
    [Required]
    public string? Subject { get; set; }

    [Required]
    public string? Body { get; set; }

    public bool IsHtml { get; set; }
}
