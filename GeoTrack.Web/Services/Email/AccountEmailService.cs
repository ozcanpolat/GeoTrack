using System.Text;
using System.Text.Encodings.Web;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace GeoTrack.Web.Services.Email;

public sealed class AccountEmailService(IEmailSender sender, IConfiguration configuration,
    IWebHostEnvironment environment, ILogger<AccountEmailService> logger)
{
    public bool IsConfigured => sender.IsConfigured;
    public IReadOnlyList<string> MissingSettings => sender.MissingSettings;

    public async Task<bool> SendConfirmationAsync(ApplicationUser user, string token, HttpRequest request, CancellationToken ct)
    {
        var url = BuildUrl("/Account/ConfirmEmail", request, new Dictionary<string, string?>
        {
            ["userId"] = user.Id, ["code"] = Encode(token)
        });
        return url is not null && await SendSafelyAsync(user.Email!, "GeoTrack e-posta doğrulaması",
            $"<p>Hesabınızı doğrulamak için <a href=\"{HtmlEncoder.Default.Encode(url)}\">bu bağlantıyı açın</a>.</p>", ct);
    }

    public Task<bool> SendVerificationCodeAsync(string recipient, string code, CancellationToken ct) =>
        SendSafelyAsync(recipient, "GeoTrack doğrulama kodunuz",
            $"<p>GeoTrack doğrulama kodunuz:</p><p style=\"font-size:28px;letter-spacing:6px;font-weight:700\">{HtmlEncoder.Default.Encode(code)}</p>" +
            "<p>Kod 10 dakika geçerlidir. İsteği siz yapmadıysanız bu e-postayı yok sayın.</p>", ct);

    public async Task<bool> SendPasswordResetAsync(ApplicationUser user, string token, HttpRequest request, CancellationToken ct)
    {
        var url = BuildUrl("/Account/ResetPassword", request, new Dictionary<string, string?>
        {
            ["email"] = user.Email, ["code"] = Encode(token)
        });
        return url is not null && await SendSafelyAsync(user.Email!, "GeoTrack parola sıfırlama",
            $"<p>Parolanızı yenilemek için <a href=\"{HtmlEncoder.Default.Encode(url)}\">bu bağlantıyı açın</a>. İsteği siz başlatmadıysanız e-postayı yok sayın.</p>", ct);
    }

    private async Task<bool> SendSafelyAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        if (!sender.IsConfigured) return false;
        try
        {
            await sender.SendAsync(recipient, subject, body, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var smtpCode = (ex as System.Net.Mail.SmtpException)?.StatusCode.ToString() ?? "-";
            logger.LogError(ex, "E-posta gönderilemedi ({Type}, SMTP durumu: {Status}). Gmail için: 2 Adımlı Doğrulama + Uygulama Şifresi, SMTP_HOST=smtp.gmail.com, SMTP_PORT=587, SMTP_USERNAME ve SMTP_FROM_EMAIL aynı Gmail adresi olmalı.", ex.GetType().Name, smtpCode);
            return false;
        }
    }

    private string? BuildUrl(string path, HttpRequest request, IDictionary<string, string?> parameters)
    {
        var configured = configuration["App:PublicBaseUrl"];
        if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri) &&
            configuredUri.Scheme is "https" or "http")
            return QueryHelpers.AddQueryString(new Uri(configuredUri, path).ToString(), parameters);

        if (environment.IsDevelopment() && request.Host.Host is "localhost" or "127.0.0.1" or "::1")
        {
            var origin = $"{request.Scheme}://{request.Host}{request.PathBase}";
            return QueryHelpers.AddQueryString(new Uri(new Uri(origin), path).ToString(), parameters);
        }
        logger.LogWarning("Account email skipped because App:PublicBaseUrl is not configured.");
        return null;
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}
