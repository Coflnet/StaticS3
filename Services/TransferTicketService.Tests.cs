using System;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Coflnet.StaticS3.Services;

[TestFixture]
public class TransferTicketServiceTests
{
    private static readonly TransferOptions Options = new()
    {
        SigningKey = "01234567890123456789012345678901",
        AdminToken = "0123456789012345",
        BucketName = "transfers",
        DefaultTtlSeconds = 3600,
        MaxTtlSeconds = 7200,
        MaxBytes = 1000
    };

    private static string ValidPublicKey() => Convert.ToBase64String(Bytes());

    private static byte[] Bytes()
    {
        var point = new byte[65];
        point[0] = 0x04;
        for (var i = 1; i < point.Length; i++)
            point[i] = (byte)i;
        return point;
    }

    [Test]
    public void RoundTripPreservesTicketFields()
    {
        var service = new TransferTicketService(Options);
        var ticket = service.Create(120, 500, "  My Label  ", ValidPublicKey());

        var token = service.Encode(ticket);
        var decoded = service.TryDecode(token, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(error, Is.EqualTo(""));
            Assert.That(result!.Id, Is.EqualTo(ticket.Id));
            Assert.That(result.ExpiresAt.ToUnixTimeSeconds(), Is.EqualTo(ticket.ExpiresAt.ToUnixTimeSeconds()));
            Assert.That(result.MaxBytes, Is.EqualTo(500));
            Assert.That(result.Label, Is.EqualTo("My Label"));
            Assert.That(result.PublicKey, Is.EqualTo(ticket.PublicKey));
        });
    }

    [Test]
    public void TamperedPayloadFailsAsInvalid()
    {
        var service = new TransferTicketService(Options);
        var ticket = service.Create(null, null, null, ValidPublicKey());
        var token = service.Encode(ticket);
        var parts = token.Split('.');
        var tamperedPayload = parts[1].Length > 0 && parts[1][0] != 'A' ? "A" + parts[1][1..] : "B" + parts[1][1..];
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var decoded = service.TryDecode(tampered, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.False);
            Assert.That(error, Is.EqualTo("invalid"));
        });
    }

    [Test]
    public void TamperedSignatureFailsAsInvalid()
    {
        var service = new TransferTicketService(Options);
        var ticket = service.Create(null, null, null, ValidPublicKey());
        var token = service.Encode(ticket);
        var parts = token.Split('.');
        var tamperedSignature = parts[2].Length > 0 && parts[2][0] != 'A' ? "A" + parts[2][1..] : "B" + parts[2][1..];
        var tampered = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        var decoded = service.TryDecode(tampered, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.False);
            Assert.That(error, Is.EqualTo("invalid"));
        });
    }

    [Test]
    public void TokenSignedWithDifferentKeyFailsAsInvalid()
    {
        var otherOptions = new TransferOptions
        {
            SigningKey = "98765432109876543210987654321098",
            AdminToken = Options.AdminToken,
            BucketName = Options.BucketName
        };
        var otherService = new TransferTicketService(otherOptions);
        var ticket = otherService.Create(null, null, null, ValidPublicKey());
        var token = otherService.Encode(ticket);

        var service = new TransferTicketService(Options);
        var decoded = service.TryDecode(token, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.False);
            Assert.That(error, Is.EqualTo("invalid"));
        });
    }

    [Test]
    public void ExpiredTicketDecodesAsExpired()
    {
        var service = new TransferTicketService(Options);
        var ticket = service.Create(60, null, null, ValidPublicKey());
        var expiredTicket = ticket with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-10) };
        var token = service.Encode(expiredTicket);

        var decoded = service.TryDecode(token, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.False);
            Assert.That(error, Is.EqualTo("expired"));
            Assert.That(result, Is.Not.Null);
        });
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("abc")]
    [TestCase("v2.a.b")]
    public void GarbageInputFailsAsInvalidWithoutThrowing(string? token)
    {
        var service = new TransferTicketService(Options);

        bool decoded = false;
        string error = "";
        Assert.DoesNotThrow(() => decoded = service.TryDecode(token, out _, out error));

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.False);
            Assert.That(error, Is.EqualTo("invalid"));
        });
    }

    [Test]
    public void TtlIsClampedToMaxTtlSeconds()
    {
        var service = new TransferTicketService(Options);

        var ticket = service.Create(999999, null, null, ValidPublicKey());

        var expectedMax = DateTimeOffset.UtcNow.AddSeconds(Options.MaxTtlSeconds);
        Assert.That(ticket.ExpiresAt, Is.LessThanOrEqualTo(expectedMax.AddSeconds(1)));
        Assert.That(ticket.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddSeconds(Options.MaxTtlSeconds - 5)));
    }

    [Test]
    public void MaxBytesIsClampedToOptionsMaxBytes()
    {
        var service = new TransferTicketService(Options);

        var ticket = service.Create(null, 999999999, null, ValidPublicKey());

        Assert.That(ticket.MaxBytes, Is.EqualTo(Options.MaxBytes));
    }

    [Test]
    public void InvalidPublicKeyThrowsArgumentException()
    {
        var service = new TransferTicketService(Options);

        Assert.Throws<ArgumentException>(() => service.Create(null, null, null, "not-base64!!!"));
        Assert.Throws<ArgumentException>(() => service.Create(null, null, null, Convert.ToBase64String(new byte[64])));
        var wrongPrefix = Bytes();
        wrongPrefix[0] = 0x02;
        Assert.Throws<ArgumentException>(() => service.Create(null, null, null, Convert.ToBase64String(wrongPrefix)));
    }

    [Test]
    public void GenerateKeyPairProducesValidPointAndJwk()
    {
        var pair = TransferTicketService.GenerateKeyPair();

        var point = Base64UrlDecode(pair.PublicKey);
        Assert.That(point.Length, Is.EqualTo(65));
        Assert.That(point[0], Is.EqualTo(0x04));

        using var document = System.Text.Json.JsonDocument.Parse(pair.PrivateKeyJwk);
        var root = document.RootElement;
        var x = Base64UrlDecode(root.GetProperty("x").GetString()!);
        var y = Base64UrlDecode(root.GetProperty("y").GetString()!);
        var d = Base64UrlDecode(root.GetProperty("d").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(point[1..33]));
            Assert.That(y, Is.EqualTo(point[33..65]));
            Assert.That(d.Length, Is.EqualTo(32));
            Assert.That(root.GetProperty("kty").GetString(), Is.EqualTo("EC"));
            Assert.That(root.GetProperty("crv").GetString(), Is.EqualTo("P-256"));
        });
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }
        return Convert.FromBase64String(normalized);
    }
}
