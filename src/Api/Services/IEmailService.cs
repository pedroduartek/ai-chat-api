namespace Api.Services;

using Api.Models;

public interface IEmailService
{
    Task SendEmailAsync(EmailRequest request, CancellationToken ct = default);
}
