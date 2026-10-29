using System.Text;
using System.Text.Json;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace GeoTrack.Web.Services.Email;

public enum IssueStatus { Sent, Cooldown, LimitReached, NotConfigured, SendFailed }
public enum VerifyStatus { Success, Invalid, Expired, TooManyAttempts, AlreadyConfirmed }
public enum RegisterStatus { CodeSent, AlreadyRegistered, InvalidPassword, CodeCooldown, LimitReached, NotConfigured, SendFailed }

public sealed record IssueResult(IssueStatus Status, int RetryAfterSeconds = 0);
public sealed record RegisterResult(RegisterStatus Status, bool IsNewUser, IReadOnlyList<string> Errors, int RetryAfterSeconds = 0, ApplicationUser? User = null);

/// <summary>E-posta doğrulama kodu akışı. Durum Identity'nin AspNetUserTokens tablosunda tutulur (şema değişikliği gerekmez).</summary>
public sealed class EmailVerificationService(UserManager<ApplicationUser> users, AccountEmailService email,
    IConfiguration configuration, ILogger<EmailVerificationService> logger)
{
    private const string Provider = "GeoTrack";
    private const string TokenName = "EmailCode";
    private byte[] Key => Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey eksik."));

    public bool IsConfigured => email.IsConfigured;

    public async Task<RegisterResult> RegisterAsync(string rawEmail, string password, CancellationToken ct)
    {
        if (!email.IsConfigured)
        {
            logger.LogError("Kayıt reddedildi: e-posta gönderimi yapılandırılmamış. Eksik değişkenler: {Missing}", string.Join(", ", email.MissingSettings));
            return new(RegisterStatus.NotConfigured, false, []);
        }
        var address = rawEmail.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(address);
        var isNew = user is null;
        if (user is not null)
        {
            if (user.EmailConfirmed || !user.IsActive) return new(RegisterStatus.AlreadyRegistered, false, []);
            // Doğrulanmamış hesap: e-posta sahibi son kayıt isteğindeki parolayı belirler; hesabı ancak kod ile doğrulayan açabilir.
            var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password);
            if (!reset.Succeeded) return new(RegisterStatus.InvalidPassword, false, reset.Errors.Select(e => e.Description).ToArray());
        }
        else
        {
            user = new ApplicationUser { UserName = address, Email = address };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                var duplicate = created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
                return duplicate ? new(RegisterStatus.AlreadyRegistered, false, [])
                    : new(RegisterStatus.InvalidPassword, false, created.Errors.Select(e => e.Description).ToArray());
            }
            var role = await users.AddToRoleAsync(user, IdentitySeeder.UserRole);
            if (!role.Succeeded)
            {
                logger.LogError("Yeni hesaba rol atanamadı; hesap geri alınıyor.");
                await users.DeleteAsync(user);
                return new(RegisterStatus.SendFailed, false, []);
            }
        }

        var issued = await IssueAsync(user, ct);
        switch (issued.Status)
        {
            case IssueStatus.Sent: return new(RegisterStatus.CodeSent, isNew, [], 0, user);
            case IssueStatus.Cooldown: return new(RegisterStatus.CodeCooldown, isNew, [], issued.RetryAfterSeconds, user);
            case IssueStatus.LimitReached: return new(RegisterStatus.LimitReached, isNew, []);
            default:
                if (isNew) await users.DeleteAsync(user); // kullanılamaz, doğrulanamayan hesap bırakılmaz
                return new(issued.Status == IssueStatus.NotConfigured ? RegisterStatus.NotConfigured : RegisterStatus.SendFailed, isNew, []);
        }
    }

    /// <summary>E-posta adresine göre yeniden kod gönderir. Hesap yoksa/doğrulanmışsa sessizce "gönderildi" döner (hesap sızdırmaz).</summary>
    public async Task<IssueResult> ResendAsync(string rawEmail, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(rawEmail.Trim());
        if (user is null || user.EmailConfirmed || !user.IsActive) return new(IssueStatus.Sent);
        return await IssueAsync(user, ct);
    }

    public async Task<IssueResult> IssueAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!email.IsConfigured) return new(IssueStatus.NotConfigured);
        var now = DateTime.UtcNow;
        var state = await LoadAsync(user);
        if (EmailCodePolicy.SendLimitReached(state, now)) return new(IssueStatus.LimitReached);
        var wait = EmailCodePolicy.CooldownSecondsLeft(state, now);
        if (wait > 0) return new(IssueStatus.Cooldown, wait);

        var code = EmailCodePolicy.NewCode();
        var next = EmailCodePolicy.Issue(state, code, user.Id, Key, now);
        if (!await email.SendVerificationCodeAsync(user.Email!, code, ct)) return new(IssueStatus.SendFailed);
        await users.SetAuthenticationTokenAsync(user, Provider, TokenName, JsonSerializer.Serialize(next));
        return new(IssueStatus.Sent);
    }

    public async Task<(VerifyStatus Status, ApplicationUser? User)> VerifyAsync(string rawEmail, string? code)
    {
        var user = await users.FindByEmailAsync(rawEmail.Trim());
        // Bilinmeyen hesap, kodsuz hesap ile aynı yanıtı verir.
        if (user is null || !user.IsActive) return (VerifyStatus.Invalid, null);
        if (user.EmailConfirmed) return (VerifyStatus.AlreadyConfirmed, user);
        var state = await LoadAsync(user);
        if (state is null) return (VerifyStatus.Expired, null);

        var (result, next) = EmailCodePolicy.Check(state, code, user.Id, Key, DateTime.UtcNow);
        if (next is null) await users.RemoveAuthenticationTokenAsync(user, Provider, TokenName);
        else if (next != state) await users.SetAuthenticationTokenAsync(user, Provider, TokenName, JsonSerializer.Serialize(next));

        switch (result)
        {
            case CodeCheck.Ok:
                var confirmed = await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
                return confirmed.Succeeded ? (VerifyStatus.Success, user) : (VerifyStatus.Invalid, null);
            case CodeCheck.Expired: return (VerifyStatus.Expired, null);
            case CodeCheck.TooManyAttempts: return (VerifyStatus.TooManyAttempts, null);
            default: return (VerifyStatus.Invalid, null);
        }
    }

    private async Task<EmailCodeState?> LoadAsync(ApplicationUser user)
    {
        var json = await users.GetAuthenticationTokenAsync(user, Provider, TokenName);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<EmailCodeState>(json); }
        catch (JsonException) { return null; }
    }
}
