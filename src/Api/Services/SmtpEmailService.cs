using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Api.Services;

public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly string? _password;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger, IConfiguration config)
    {
        _options = options.Value;
        _logger = logger;
        // Read SMTP key from environment (local .env) or configuration
        _password = Environment.GetEnvironmentVariable("SMTP_KEY") ?? config["SMTP_KEY"];
    }

    public async Task SendEmailAsync(Api.Models.EmailRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(_options.SmtpHost)) throw new InvalidOperationException("SMTP host not configured");
        if (string.IsNullOrEmpty(_options.From)) throw new InvalidOperationException("From address not configured");
        if (string.IsNullOrEmpty(_password)) throw new InvalidOperationException("SMTP key (SMTP_KEY) not set in environment");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));

        message.To.Add(MailboxAddress.Parse("pedroduartek@gmail.com"));

        message.Subject = request.Subject ?? string.Empty;
        var builder = new BodyBuilder();
        if (request.IsHtml) builder.HtmlBody = request.Body;
        else builder.TextBody = request.Body;
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
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
            _logger.LogInformation("Email sent to pedroduartek@gmail.com");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            throw;
        }
    }
}
