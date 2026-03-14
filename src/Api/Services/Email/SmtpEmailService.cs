using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using MailKit.Net.Smtp;
using MailKit.Security;
using Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Api.Services.Email;

public class SmtpEmailService : IEmailService
{
    private const string SmtpKeyConfigurationKey = "SMTP_KEY";

    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly string? _password;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger, IConfiguration config)
    {
        _options = options.Value;
        _logger = logger;
        _password = config[SmtpKeyConfigurationKey];
    }

    public async Task SendEmailAsync(Api.Models.EmailRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(_options.SmtpHost)) throw new InvalidOperationException("SMTP host not configured");
        if (string.IsNullOrEmpty(_options.From)) throw new InvalidOperationException("From address not configured");
        if (string.IsNullOrEmpty(_options.Recipient)) throw new InvalidOperationException("Recipient address not configured");
        if (string.IsNullOrEmpty(_password)) throw new InvalidOperationException("SMTP key (SMTP_KEY) not set in configuration");

        var normalizedName = request.Name!.Trim();
        var normalizedEmail = request.Email!.Trim();
        var normalizedSubject = request.Subject!.Trim();
        var normalizedMessage = request.Message!.Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(request.Source) ? "contact form" : request.Source.Trim();

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(_options.Recipient));
        message.ReplyTo.Add(new MailboxAddress(normalizedName, normalizedEmail));

        message.Subject = normalizedSubject;
        var builder = new BodyBuilder();
        builder.TextBody = BuildPlainTextBody(normalizedName, normalizedEmail, normalizedSubject, normalizedMessage, normalizedSource);
        builder.HtmlBody = BuildHtmlBody(normalizedName, normalizedEmail, normalizedSubject, normalizedMessage, normalizedSource);
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = _options.TimeoutSeconds * 1000;
        var sw = Stopwatch.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SmtpHost"] = _options.SmtpHost,
            ["SmtpPort"] = _options.SmtpPort,
            ["From"] = _options.From,
            ["Recipient"] = _options.Recipient,
            ["SubjectLength"] = normalizedSubject.Length,
            ["BodyLength"] = normalizedMessage.Length,
            ["Source"] = normalizedSource
        });
        try
        {
            _logger.LogInformation("SMTP send started");
            var secure = _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_options.SmtpHost!, _options.SmtpPort, secure, ct);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _password!, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            sw.Stop();
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["DurationMs"] = sw.ElapsedMilliseconds
            }))
            {
                _logger.LogInformation("SMTP send completed");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["DurationMs"] = sw.ElapsedMilliseconds
            }))
            {
                _logger.LogError(ex, "SMTP send failed");
            }
            throw;
        }
    }

    private static string BuildPlainTextBody(string name, string email, string subject, string message, string source)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"New message from pedroduartek.com {source}",
            string.Empty,
            $"Name: {name}",
            $"Email: {email}",
            $"Subject: {subject}",
            string.Empty,
            "Message:",
            message
        });
    }

    private static string BuildHtmlBody(string name, string email, string subject, string message, string source)
    {
        var encodedName = WebUtility.HtmlEncode(name);
        var encodedEmail = WebUtility.HtmlEncode(email);
        var encodedSubject = WebUtility.HtmlEncode(subject);
        var encodedMessage = WebUtility.HtmlEncode(message).Replace("\n", "<br/>", StringComparison.Ordinal);
        var encodedSource = WebUtility.HtmlEncode(source);

        return string.Join(Environment.NewLine, new[]
        {
            $"<p>New message from pedroduartek.com {encodedSource}</p>",
            $"<p><strong>Name:</strong> {encodedName}<br/><strong>Email:</strong> {encodedEmail}</p>",
            $"<p><strong>Subject:</strong> {encodedSubject}</p>",
            "<p><strong>Message:</strong></p>",
            $"<p>{encodedMessage}</p>"
        });
    }
}
