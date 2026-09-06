using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Coflnet.StaticS3.Services;

[TestFixture]
public class TransferOptionsTests
{
    private const string SigningKey = "0123456789abcdef0123456789abcdef";
    private const string AdminToken = "admin-token-1234";

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
            settings[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // appsettings.json ships Transfer:* as empty strings; those must not shadow the env fallback.
    [Test]
    public void EnvironmentFallbackWinsOverBlankAppSettingsValues()
    {
        var options = TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", ""),
            ("Transfer:AdminToken", ""),
            ("Transfer:BucketName", ""),
            ("Transfer:MaxBytes", ""),
            ("TRANSFER_SIGNING_KEY", SigningKey),
            ("TRANSFER_ADMIN_TOKEN", AdminToken),
            ("TRANSFER_BUCKET_NAME", "transfers"),
            ("TRANSFER_MAX_BYTES", "1048576")), "static");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.SigningKey, Is.EqualTo(SigningKey));
            Assert.That(options.AdminToken, Is.EqualTo(AdminToken));
            Assert.That(options.BucketName, Is.EqualTo("transfers"));
            Assert.That(options.MaxBytes, Is.EqualTo(1048576));
        });
    }

    [Test]
    public void ExplicitSectionValuesWinOverEnvironment()
    {
        var options = TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", SigningKey),
            ("Transfer:AdminToken", AdminToken),
            ("Transfer:BucketName", "from-section"),
            ("TRANSFER_BUCKET_NAME", "from-environment")), "static");

        Assert.That(options.BucketName, Is.EqualTo("from-section"));
    }

    [Test]
    public void StaysDisabledWithoutThrowingWhenNothingIsConfigured()
    {
        var options = TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", ""),
            ("Transfer:AdminToken", ""),
            ("Transfer:BucketName", "")), "static");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.KeyPrefix, Is.EqualTo("transfers"));
            Assert.That(options.MaxBytes, Is.EqualTo(268435456));
        });
    }

    [Test]
    public void RefusesToShareTheBucketWithPublicStaticObjects()
    {
        Assert.Throws<InvalidOperationException>(() => TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", SigningKey),
            ("Transfer:AdminToken", AdminToken),
            ("Transfer:BucketName", "STATIC")), "static"));
    }

    [Test]
    public void RejectsWeakSigningKeyAndAdminToken()
    {
        Assert.Throws<InvalidOperationException>(() => TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", "too-short"),
            ("Transfer:AdminToken", AdminToken),
            ("Transfer:BucketName", "transfers")), "static"));
        Assert.Throws<InvalidOperationException>(() => TransferOptions.FromConfiguration(Configuration(
            ("Transfer:SigningKey", SigningKey),
            ("Transfer:AdminToken", "short"),
            ("Transfer:BucketName", "transfers")), "static"));
    }
}
