using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;
using GeoTrack.Web.Services.Email;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace GeoTrack.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous().WithTags("Authentication");
        group.MapPost("/register", async (RegisterRequest request, EmailVerificationService verification,
            GeoTrackDbContext db, AuditLogService audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !new EmailAddressAttribute().IsValid(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Geçerli e-posta ve parola gerekli."] });
            var result = await verification.RegisterAsync(request.Email, request.Password, ct);
            switch (result.Status)
            {
                case RegisterStatus.CodeSent:
                case RegisterStatus.CodeCooldown:
                    if (result is { IsNewUser: true, User: { } created })
                    {
                        audit.Add(created.Id, "AccountRegistered", "User", created.Id, "Yeni hesap kaydı oluşturuldu.");
                        await db.SaveChangesAsync(ct);
                    }
                    return Results.Accepted(value: new RegistrationResponse(true));
                case RegisterStatus.AlreadyRegistered:
                    return Results.Problem(title: "E-posta zaten kayıtlı", detail: "Bu e-posta adresi zaten kayıtlı.", statusCode: StatusCodes.Status409Conflict);
                case RegisterStatus.InvalidPassword:
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = result.Errors.Count > 0 ? result.Errors.ToArray() : ["Parola gereksinimleri karşılanmıyor."] });
                case RegisterStatus.LimitReached:
                    return Results.Problem(title: "Çok fazla kod istendi", detail: "Bir saat sonra tekrar deneyin.", statusCode: StatusCodes.Status429TooManyRequests);
                case RegisterStatus.NotConfigured:
                    return Results.Problem(title: "E-posta hizmeti kullanılamıyor", detail: "Doğrulama kodu gönderilemediği için hesap oluşturulmadı.", statusCode: StatusCodes.Status503ServiceUnavailable);
                default:
                    return Results.Problem(title: "Doğrulama kodu gönderilemedi", detail: "Hesap oluşturulmadı; daha sonra tekrar deneyin.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("auth").WithName("Register");

        group.MapPost("/verify-email", async (VerifyEmailRequest request, EmailVerificationService verification,
            GeoTrackDbContext db, AuditLogService audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { message = "E-posta ve kod gerekli." });
            var (status, user) = await verification.VerifyAsync(request.Email, request.Code.Trim());
            if (status == VerifyStatus.Success)
            {
                audit.Add(user!.Id, "EmailConfirmed", "User", user.Id, "E-posta doğrulama koduyla doğrulandı.");
                await db.SaveChangesAsync(ct);
            }
            return status switch
            {
                VerifyStatus.Success or VerifyStatus.AlreadyConfirmed => Results.Ok(new { confirmed = true }),
                VerifyStatus.Expired => Results.BadRequest(new { message = "Kodun süresi dolmuş veya aktif kod yok. Yeni kod isteyin.", code = "expired" }),
                VerifyStatus.TooManyAttempts => Results.BadRequest(new { message = "Çok fazla hatalı deneme. Yeni kod isteyin.", code = "too_many_attempts" }),
                _ => Results.BadRequest(new { message = "Kod geçersiz.", code = "invalid" })
            };
        }).RequireRateLimiting("auth").WithName("VerifyEmail");

        group.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, JwtTokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Problem(title: "Giriş başarısız", statusCode: StatusCodes.Status401Unauthorized);
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsActive || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            {
                return Results.Problem(title: "Giriş başarısız", statusCode: StatusCodes.Status401Unauthorized);
            }
            if (!user.EmailConfirmed)
                return Results.Problem(title: "E-posta doğrulaması gerekli", detail: "Giriş yapmadan önce e-posta adresinizi doğrulayın.", statusCode: StatusCodes.Status403Forbidden);
            if (user.TwoFactorEnabled)
                return Results.Accepted("/api/auth/login/two-factor", new { requiresTwoFactor = true });
            var token = await tokens.CreateAsync(user);
            return Results.Ok(new AuthResponse(token.Token, token.ExpiresAt, user.Email!));
        }).RequireRateLimiting("auth").WithName("Login");

        group.MapPost("/login/two-factor", async (TwoFactorLoginRequest request, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, JwtTokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Code))
                return Results.Problem(title: "Giriş başarısız", statusCode: StatusCodes.Status401Unauthorized);
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsActive || !user.EmailConfirmed || !user.TwoFactorEnabled ||
                !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
                return Results.Problem(title: "Giriş başarısız", statusCode: StatusCodes.Status401Unauthorized);

            var verified = request.IsRecoveryCode
                ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Trim())).Succeeded
                : await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
                    request.Code.Replace(" ", "").Replace("-", ""));
            if (!verified)
            {
                await users.AccessFailedAsync(user);
                return Results.Problem(title: "Giriş başarısız", statusCode: StatusCodes.Status401Unauthorized);
            }
            await users.ResetAccessFailedCountAsync(user);
            var token = await tokens.CreateAsync(user);
            return Results.Ok(new AuthResponse(token.Token, token.ExpiresAt, user.Email!));
        }).RequireRateLimiting("auth").WithName("LoginTwoFactor");

        group.MapPost("/confirm-email", async (ConfirmEmailRequest request, UserManager<ApplicationUser> users,
            GeoTrackDbContext db, AuditLogService audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş." });
            var user = await users.FindByIdAsync(request.UserId);
            if (user is null) return Results.BadRequest(new { message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş." });
            var result = await users.ConfirmEmailAsync(user, Decode(request.Code));
            if (result.Succeeded)
            {
                audit.Add(user.Id, "EmailConfirmed", "User", user.Id, "E-posta doğrulandı.");
                await db.SaveChangesAsync(ct);
            }
            return result.Succeeded ? Results.Ok(new { confirmed = true }) : Results.BadRequest(new { message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş." });
        }).RequireRateLimiting("auth").WithName("ConfirmEmail");

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, UserManager<ApplicationUser> users,
            AccountEmailService email, HttpContext context, CancellationToken ct) =>
        {
            if (!new EmailAddressAttribute().IsValid(request.Email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Geçerli e-posta adresi girin."] });
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is { EmailConfirmed: true, IsActive: true })
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                await email.SendPasswordResetAsync(user, token, context.Request, ct);
            }
            return Results.Accepted(value: new { message = "Adres kayıtlıysa parola yenileme yönergeleri e-posta ile gönderilir." });
        }).RequireRateLimiting("auth").WithName("ForgotPassword");

        group.MapPost("/reset-password", async (ResetPasswordRequest request, UserManager<ApplicationUser> users,
            GeoTrackDbContext db, AuditLogService audit, CancellationToken ct) =>
        {
            if (!new EmailAddressAttribute().IsValid(request.Email) || string.IsNullOrEmpty(request.Code) ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length < 12)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Geçerli e-posta ve en az 12 karakterli parola gerekli."] });
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null) return Results.BadRequest(new { message = "Parola yenilenemedi. Bağlantı geçersiz veya süresi dolmuş olabilir." });
            var result = await users.ResetPasswordAsync(user, Decode(request.Code), request.Password);
            if (!result.Succeeded)
                return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            await users.UpdateSecurityStampAsync(user);
            audit.Add(user.Id, "PasswordReset", "User", user.Id, "Parola sıfırlandı; önceki token'lar iptal edildi.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth").WithName("ResetPassword");

        group.MapPost("/resend-confirmation", async (ResendConfirmationRequest request, EmailVerificationService verification, CancellationToken ct) =>
        {
            if (new EmailAddressAttribute().IsValid(request.Email))
                await verification.ResendAsync(request.Email, ct); // sonuç sızdırılmaz: hesap varlığı ve bekleme süresi gösterilmez
            return Results.Accepted(value: new { message = "Adres doğrulama bekliyorsa yeni kod gönderilir." });
        }).RequireRateLimiting("auth").WithName("ResendConfirmation");
        return app;
    }

    private static string Decode(string value)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)); }
        catch (FormatException) { return string.Empty; }
    }
}

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, string Email);
public sealed record RegistrationResponse(bool EmailConfirmationRequired);
public sealed record TwoFactorLoginRequest(string Email, string Password, string Code, bool IsRecoveryCode = false);
public sealed record VerifyEmailRequest(string Email, string Code);
public sealed record ConfirmEmailRequest(string UserId, string Code);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Code, string Password);
public sealed record ResendConfirmationRequest(string Email);
