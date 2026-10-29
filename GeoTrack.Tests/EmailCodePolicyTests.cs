using GeoTrack.Web.Services.Email;
using Xunit;

namespace GeoTrack.Tests;

public class EmailCodePolicyTests
{
    private static readonly byte[] Key = System.Text.Encoding.UTF8.GetBytes("test-key-test-key-test-key-test-key!");
    private static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewCode_IsSixDigits()
    {
        for (var i = 0; i < 50; i++) Assert.True(EmailCodePolicy.IsWellFormed(EmailCodePolicy.NewCode()));
    }

    [Fact]
    public void Issue_DoesNotStoreThePlainCode()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        Assert.DoesNotContain("123456", state.Hash);
        Assert.Equal(T0.AddMinutes(10), state.ExpiresUtc);
    }

    [Fact]
    public void Check_CorrectCode_Succeeds_AndClearsState()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        var (result, next) = EmailCodePolicy.Check(state, "123456", "u1", Key, T0.AddMinutes(5));
        Assert.Equal(CodeCheck.Ok, result);
        Assert.Null(next);
    }

    [Fact]
    public void Check_CodeForAnotherUser_Fails()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        Assert.Equal(CodeCheck.Invalid, EmailCodePolicy.Check(state, "123456", "u2", Key, T0).Result);
    }

    [Fact]
    public void Check_ExpiredCode_ReturnsExpired()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        var (result, next) = EmailCodePolicy.Check(state, "123456", "u1", Key, T0.AddMinutes(10).AddSeconds(1));
        Assert.Equal(CodeCheck.Expired, result);
        Assert.Null(next);
    }

    [Fact]
    public void Check_WrongCode_CountsAttempts_ThenLocks_EvenForCorrectCode()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        CodeCheck last = CodeCheck.Invalid;
        for (var i = 0; i < EmailCodePolicy.MaxAttempts; i++)
        {
            var (result, next) = EmailCodePolicy.Check(state, "000000", "u1", Key, T0);
            last = result;
            state = next!;
        }
        Assert.Equal(CodeCheck.TooManyAttempts, last);
        Assert.Equal(CodeCheck.TooManyAttempts, EmailCodePolicy.Check(state, "123456", "u1", Key, T0).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12345a")]
    [InlineData("1234567")]
    public void Check_MalformedCode_IsInvalid(string? code)
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        Assert.Equal(CodeCheck.Invalid, EmailCodePolicy.Check(state, code, "u1", Key, T0).Result);
    }

    [Fact]
    public void Resend_RespectsCooldown()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        Assert.True(EmailCodePolicy.CooldownSecondsLeft(state, T0.AddSeconds(10)) is > 0 and <= 50);
        Assert.Equal(0, EmailCodePolicy.CooldownSecondsLeft(state, T0.AddSeconds(61)));
        Assert.Equal(0, EmailCodePolicy.CooldownSecondsLeft(null, T0));
    }

    [Fact]
    public void Resend_LimitsSendsPerHour_AndResetsAfterWindow()
    {
        EmailCodeState? state = null;
        var now = T0;
        for (var i = 0; i < EmailCodePolicy.MaxSendsPerWindow; i++)
        {
            Assert.False(EmailCodePolicy.SendLimitReached(state, now));
            state = EmailCodePolicy.Issue(state, EmailCodePolicy.NewCode(), "u1", Key, now);
            now = now.AddSeconds(61);
        }
        Assert.True(EmailCodePolicy.SendLimitReached(state, now));
        Assert.False(EmailCodePolicy.SendLimitReached(state, T0.AddHours(1).AddSeconds(1)));
    }

    [Fact]
    public void Reissue_ResetsAttemptCounter()
    {
        var state = EmailCodePolicy.Issue(null, "123456", "u1", Key, T0);
        state = EmailCodePolicy.Check(state, "000000", "u1", Key, T0).Next!;
        var fresh = EmailCodePolicy.Issue(state, "654321", "u1", Key, T0.AddSeconds(61));
        Assert.Equal(0, fresh.Attempts);
        Assert.Equal(2, fresh.SendsInWindow);
    }
}
