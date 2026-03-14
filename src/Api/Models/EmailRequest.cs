using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Api.Models;

public sealed partial class EmailRequest : IValidatableObject
{
    public const int MaxNameLength = 100;
    public const int MaxSubjectLength = 160;
    public const int MaxMessageLength = 4000;
    public const int MaxSourceLength = 32;
    public const int MaxUrlCount = 2;

    [Required]
    [StringLength(MaxNameLength, MinimumLength = 2)]
    public string? Name { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }

    [Required]
    [StringLength(MaxSubjectLength, MinimumLength = 3)]
    public string? Subject { get; set; }

    [Required]
    [StringLength(MaxMessageLength, MinimumLength = 10)]
    public string? Message { get; set; }

    [StringLength(MaxSourceLength)]
    public string? Source { get; set; }

    [StringLength(0, ErrorMessage = "Unexpected field.")]
    public string? Company { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ContainsLineBreaks(Name))
            yield return new ValidationResult("Name must be a single line.", [nameof(Name)]);

        if (ContainsLineBreaks(Subject))
            yield return new ValidationResult("Subject must be a single line.", [nameof(Subject)]);

        if (ContainsHtml(Name))
            yield return new ValidationResult("Name cannot contain HTML.", [nameof(Name)]);

        if (ContainsHtml(Subject))
            yield return new ValidationResult("Subject cannot contain HTML.", [nameof(Subject)]);

        if (ContainsHtml(Message))
            yield return new ValidationResult("Message cannot contain HTML.", [nameof(Message)]);

        if (!string.IsNullOrWhiteSpace(Source))
        {
            var normalizedSource = Source.Trim().ToLowerInvariant();
            if (normalizedSource is not ("contact form" or "terminal"))
                yield return new ValidationResult("Unsupported source.", [nameof(Source)]);
        }

        var totalUrlCount = CountUrls(Subject) + CountUrls(Message);
        if (totalUrlCount > MaxUrlCount)
            yield return new ValidationResult("Please remove extra links from your message.", [nameof(Message)]);
    }

    private static bool ContainsLineBreaks(string? value) =>
        !string.IsNullOrEmpty(value) && (value.Contains('\r') || value.Contains('\n'));

    private static bool ContainsHtml(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HtmlTagPattern().IsMatch(value);

    private static int CountUrls(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0 : UrlPattern().Matches(value).Count;

    [GeneratedRegex(@"<[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 250)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"(https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 250)]
    private static partial Regex UrlPattern();
}
