using System;
using System.Threading;
using System.Threading.Tasks;
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

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(_options.Recipient));

        message.Subject = request.Subject ?? string.Empty;
        var builder = new BodyBuilder();
        if (request.IsHtml) builder.HtmlBody = request.Body;
        else builder.TextBody = request.Body;
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = _options.TimeoutSeconds * 1000;
        try
        {
            _logger.LogInformation("Connecting to SMTP {Host}:{Port}", _options.SmtpHost, _options.SmtpPort);
            var secure = _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_options.SmtpHost!, _options.SmtpPort, secure, ct);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _password!, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            _logger.LogInformation("Email sent to {Recipient}", _options.Recipient);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            throw;
        }
    }
}
