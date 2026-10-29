namespace GeoTrack.Web.Services.Email;

public interface IEmailSender
{
    bool IsConfigured { get; }
    IReadOnlyList<string> MissingSettings { get; }
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default);
}
