using System.ComponentModel.DataAnnotations;
using Api.Models;
using Xunit;

namespace Api.Tests;

public class EmailRequestTests
{
    [Fact]
    public void Validate_AllowsStructuredContactPayload()
    {
        var request = BuildRequest();

        var results = Validate(request);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_RejectsHtmlInMessage()
    {
        var request = BuildRequest();
        request.Message = "<b>Hello</b>";

        var results = Validate(request);

        Assert.Contains(results, result => result.ErrorMessage == "Message cannot contain HTML.");
    }

    [Fact]
    public void Validate_RejectsTooManyLinks()
    {
        var request = BuildRequest();
        request.Message = "See https://one.example.com and https://two.example.com and https://three.example.com";

        var results = Validate(request);

        Assert.Contains(results, result => result.ErrorMessage == "Please remove extra links from your message.");
    }

    [Fact]
    public void Validate_RejectsUnsupportedSource()
    {
        var request = BuildRequest();
        request.Source = "script";

        var results = Validate(request);

        Assert.Contains(results, result => result.ErrorMessage == "Unsupported source.");
    }

    [Fact]
    public void Validate_RejectsHoneypotField()
    {
        var request = BuildRequest();
        request.Company = "Spam Corp";

        var results = Validate(request);

        Assert.Contains(results, result => result.ErrorMessage == "Unexpected field.");
    }

    private static EmailRequest BuildRequest() => new()
    {
        Name = "Ada Lovelace",
        Email = "ada@example.com",
        Subject = "Hello there",
        Message = "I would like to talk about a backend role.",
        Source = "contact form"
    };

    private static List<ValidationResult> Validate(EmailRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
