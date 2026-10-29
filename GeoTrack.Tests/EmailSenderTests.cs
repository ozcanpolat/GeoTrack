using GeoTrack.Web.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoTrack.Tests;

public class EmailSenderTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Gmail_WithoutCredentials_IsNotConfigured_AndNamesMissingSettings()
    {
        var sender = new SmtpEmailSender(Config(("Email:SmtpHost", "smtp.gmail.com"), ("Email:SmtpPort", "587")));
        Assert.False(sender.IsConfigured);
        Assert.Contains(sender.MissingSettings, m => m.StartsWith("SMTP_USERNAME"));
        Assert.Contains(sender.MissingSettings, m => m.StartsWith("SMTP_FROM_EMAIL"));
    }

    [Fact]
    public void Username_WithoutPassword_IsNotConfigured()
    {
        var sender = new SmtpEmailSender(Config(("Email:SmtpHost", "smtp.gmail.com"), ("Email:Username", "a@example.test")));
        Assert.False(sender.IsConfigured);
        Assert.Equal(["SMTP_PASSWORD"], sender.MissingSettings);
    }

    [Fact]
    public void Gmail_WithUsernameAndPassword_IsConfigured_FromDefaultsToUsername()
    {
        var sender = new SmtpEmailSender(Config(("Email:SmtpHost", "smtp.gmail.com"), ("Email:Username", "a@example.test"), ("Email:Password", "x")));
        Assert.True(sender.IsConfigured);
        Assert.Empty(sender.MissingSettings);
    }

    [Fact]
    public void LocalSmtp4Dev_WithoutCredentials_IsConfigured()
    {
        var sender = new SmtpEmailSender(Config(("Email:SmtpHost", "smtp4dev"), ("Email:SmtpPort", "25"), ("Email:From", "noreply@geotrack.local")));
        Assert.True(sender.IsConfigured);
    }

    private sealed class FakeSender(bool configured, Exception? failure = null) : IEmailSender
    {
        public bool IsConfigured => configured;
        public IReadOnlyList<string> MissingSettings => configured ? [] : ["SMTP_HOST"];
        public string? LastBody { get; private set; }
        public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (failure is not null) throw failure;
            LastBody = htmlBody;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnv : IHostEnvironment, Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "/";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static AccountEmailService Service(IEmailSender sender) =>
        new(sender, Config(), new FakeEnv(), NullLogger<AccountEmailService>.Instance);

    [Fact]
    public async Task SendVerificationCode_Success_ContainsCode()
    {
        var fake = new FakeSender(true);
        Assert.True(await Service(fake).SendVerificationCodeAsync("u@example.test", "123456", default));
        Assert.Contains("123456", fake.LastBody);
    }

    [Fact]
    public async Task SendVerificationCode_SmtpFailure_ReturnsFalse_InsteadOfThrowing() =>
        Assert.False(await Service(new FakeSender(true, new System.Net.Mail.SmtpException("auth failed"))).SendVerificationCodeAsync("u@example.test", "123456", default));

    [Fact]
    public async Task SendVerificationCode_WhenNotConfigured_ReturnsFalse() =>
        Assert.False(await Service(new FakeSender(false)).SendVerificationCodeAsync("u@example.test", "123456", default));
}
