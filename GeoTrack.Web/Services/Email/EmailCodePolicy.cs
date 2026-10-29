using System.Security.Cryptography;
using System.Text;

namespace GeoTrack.Web.Services.Email;

/// <summary>Saklanan doğrulama kodu durumu. Kodun kendisi tutulmaz; yalnızca HMAC özeti saklanır.</summary>
public sealed record EmailCodeState(string Hash, DateTime ExpiresUtc, int Attempts, DateTime LastSentUtc, DateTime WindowStartUtc, int SendsInWindow);

public enum CodeCheck { Ok, Invalid, Expired, TooManyAttempts }

/// <summary>6 haneli e-posta doğrulama kodları için saf (durumsuz) kurallar: süre, deneme ve gönderim sınırları.</summary>
public static class EmailCodePolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    public const int MaxSendsPerWindow = 5;
    public const int MaxAttempts = 5;

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static bool IsWellFormed(string? code) => code is { Length: 6 } && code.All(char.IsAsciiDigit);

    public static string Hash(string code, string userId, byte[] key)
    {
        var bytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(userId + ":" + code));
        return Convert.ToHexString(bytes);
    }

    public static int CooldownSecondsLeft(EmailCodeState? state, DateTime now)
    {
        if (state is null) return 0;
        var left = state.LastSentUtc + ResendCooldown - now;
        return left > TimeSpan.Zero ? (int)Math.Ceiling(left.TotalSeconds) : 0;
    }

    public static bool SendLimitReached(EmailCodeState? state, DateTime now) =>
        state is not null && now - state.WindowStartUtc < SendWindow && state.SendsInWindow >= MaxSendsPerWindow;

    public static EmailCodeState Issue(EmailCodeState? previous, string code, string userId, byte[] key, DateTime now)
    {
        var windowOpen = previous is not null && now - previous.WindowStartUtc < SendWindow;
        return new EmailCodeState(Hash(code, userId, key), now + Lifetime, 0, now,
            windowOpen ? previous!.WindowStartUtc : now, windowOpen ? previous!.SendsInWindow + 1 : 1);
    }

    /// <summary>Kodu denetler. Dönen durum, saklanacak yeni durumdur (null = kaydı sil).</summary>
    public static (CodeCheck Result, EmailCodeState? Next) Check(EmailCodeState state, string? code, string userId, byte[] key, DateTime now)
    {
        if (now > state.ExpiresUtc) return (CodeCheck.Expired, null);
        if (state.Attempts >= MaxAttempts) return (CodeCheck.TooManyAttempts, state);
        var candidate = IsWellFormed(code) ? Hash(code!, userId, key) : new string('0', 64);
        var match = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(state.Hash));
        if (match && IsWellFormed(code)) return (CodeCheck.Ok, null);
        var next = state with { Attempts = state.Attempts + 1 };
        return (next.Attempts >= MaxAttempts ? CodeCheck.TooManyAttempts : CodeCheck.Invalid, next);
    }
}
