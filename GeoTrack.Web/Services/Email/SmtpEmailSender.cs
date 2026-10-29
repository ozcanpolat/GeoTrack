using System.Net;
using System.Net.Mail;

namespace GeoTrack.Web.Services.Email;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private string? Host => configuration["Email:SmtpHost"];
    private string? From => string.IsNullOrWhiteSpace(configuration["Email:From"]) ? configuration["Email:Username"] : configuration["Email:From"];
    public bool IsConfigured => MissingSettings.Count == 0;

    /// <summary>Eksik ortam değişkenlerinin adları (değerleri asla içermez).</summary>
    public IReadOnlyList<string> MissingSettings
    {
        get
        {
            var missing = new List<string>();
            var username = configuration["Email:Username"];
            if (string.IsNullOrWhiteSpace(Host)) missing.Add("SMTP_HOST");
            if (string.IsNullOrWhiteSpace(From)) missing.Add("SMTP_FROM_EMAIL (veya SMTP_USERNAME)");
            if (Host?.Contains("gmail", StringComparison.OrdinalIgnoreCase) == true && string.IsNullOrWhiteSpace(username)) missing.Add("SMTP_USERNAME (Gmail için zorunlu)");
            if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(configuration["Email:Password"])) missing.Add("SMTP_PASSWORD");
            return missing;
        }
    }

    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış.");
        var port = int.TryParse(configuration["Email:SmtpPort"], out var configuredPort) ? configuredPort : 587;
        using var message = new MailMessage { From = new MailAddress(From!, configuration["Email:FromName"] ?? "GeoTrack"), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        message.To.Add(recipient);
        using var client = new SmtpClient(Host!, port)
        {
            // Kimlik doğrulamalı sunucuda (Gmail 587) STARTTLS zorunlu; kimlik bilgisiz yerel test sunucusunda (smtp4dev) şifreleme yok.
            EnableSsl = !string.IsNullOrWhiteSpace(configuration["Email:Username"]) || (bool.TryParse(configuration["Email:EnableSsl"], out var ssl) && ssl),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"]?.Replace(" ", ""); // Google uygulama şifresi boşluklu gösterilir
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        await client.SendMailAsync(message, ct);
    }
}
