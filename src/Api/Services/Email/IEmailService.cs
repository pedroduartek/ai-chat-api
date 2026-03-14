using Api.Models;

namespace Api.Services.Email;

public interface IEmailService
{
    Task SendEmailAsync(EmailRequest request, CancellationToken ct = default);
}
