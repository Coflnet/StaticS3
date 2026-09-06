using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coflnet.StaticS3.Services;

public sealed record UploadTicket(string Id, DateTimeOffset ExpiresAt, long MaxBytes, string PublicKey, string? Label);

public sealed record TransferKeyPair(string PublicKey, string PrivateKeyJwk);

public sealed class TransferTicketService
{
    private readonly TransferOptions options;

    public TransferTicketService(TransferOptions options)
    {
        this.options = options;
    }

    public UploadTicket Create(int? ttlSeconds, long? maxBytes, string? label, string? publicKey)
    {
        var ttl = Math.Clamp(ttlSeconds ?? options.DefaultTtlSeconds, 60, options.MaxTtlSeconds);
        var max = Math.Clamp(maxBytes ?? options.MaxBytes, 1, options.MaxBytes);
        var trimmedLabel = label?.Trim();
        if (string.IsNullOrEmpty(trimmedLabel))
            trimmedLabel = null;
        else if (trimmedLabel.Length > 120)
            trimmedLabel = trimmedLabel[..120];
        var normalizedKey = NormalizePublicKey(publicKey);
        var id = Base64UrlEncode(RandomNumberGenerator.GetBytes(8));
        return new UploadTicket(id, DateTimeOffset.UtcNow.AddSeconds(ttl), max, normalizedKey, trimmedLabel);
    }

    public string Encode(UploadTicket ticket)
    {
        var payload = new TicketPayload
        {
            Id = ticket.Id,
            Exp = ticket.ExpiresAt.ToUnixTimeSeconds(),
            Max = ticket.MaxBytes,
            Pk = ticket.PublicKey,
            Lbl = ticket.Label
        };
        var payloadB64Url = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var signature = Base64UrlEncode(ComputeSignature(payloadB64Url));
        return $"v1.{payloadB64Url}.{signature}";
    }

    public bool TryDecode(string? token, out UploadTicket? ticket, out string error)
    {
        ticket = null;
        error = "invalid";
        if (string.IsNullOrEmpty(token))
            return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1")
            return false;

        try
        {
            var signatureBytes = Base64UrlDecode(parts[2]);
            var expectedSignature = ComputeSignature(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature))
                return false;

            var payloadBytes = Base64UrlDecode(parts[1]);
            var payload = JsonSerializer.Deserialize<TicketPayload>(payloadBytes);
            if (payload == null || string.IsNullOrEmpty(payload.Id) || string.IsNullOrEmpty(payload.Pk))
                return false;

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
            var candidate = new UploadTicket(payload.Id, expiresAt, payload.Max, payload.Pk, payload.Lbl);
            if (expiresAt < DateTimeOffset.UtcNow)
            {
                ticket = candidate;
                error = "expired";
                return false;
            }
            ticket = candidate;
            error = "";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static TransferKeyPair GenerateKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(true);
        var x = PadTo32(parameters.Q.X!);
        var y = PadTo32(parameters.Q.Y!);
        var d = PadTo32(parameters.D!);

        var point = new byte[65];
        point[0] = 0x04;
        Buffer.BlockCopy(x, 0, point, 1, 32);
        Buffer.BlockCopy(y, 0, point, 33, 32);

        var jwk = new JsonWebKey
        {
            D = Base64UrlEncode(d),
            X = Base64UrlEncode(x),
            Y = Base64UrlEncode(y)
        };
        return new TransferKeyPair(Base64UrlEncode(point), JsonSerializer.Serialize(jwk));
    }

    private byte[] ComputeSignature(string payloadB64Url)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SigningKey));
        return hmac.ComputeHash(Encoding.ASCII.GetBytes(payloadB64Url));
    }

    private static string NormalizePublicKey(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            throw new ArgumentException("A publicKey is required.", nameof(publicKey));
        byte[] bytes;
        try
        {
            bytes = Base64UrlDecode(publicKey);
        }
        catch (FormatException)
        {
            throw new ArgumentException("publicKey must be base64url or base64 encoded.", nameof(publicKey));
        }
        if (bytes.Length != 65 || bytes[0] != 0x04)
            throw new ArgumentException("publicKey must be a 65-byte uncompressed P-256 point starting with 0x04.", nameof(publicKey));
        return Base64UrlEncode(bytes);
    }

    private static byte[] PadTo32(byte[] value)
    {
        if (value.Length == 32)
            return value;
        var result = new byte[32];
        Buffer.BlockCopy(value, 0, result, 32 - value.Length, value.Length);
        return result;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Trim().Replace('-', '+').Replace('_', '/').TrimEnd('=');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
            case 1: throw new FormatException("Invalid base64 length.");
        }
        return Convert.FromBase64String(normalized);
    }

    private sealed record TicketPayload
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("exp")] public long Exp { get; init; }
        [JsonPropertyName("max")] public long Max { get; init; }
        [JsonPropertyName("pk")] public string Pk { get; init; } = "";
        [JsonPropertyName("lbl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Lbl { get; init; }
    }

    private sealed record JsonWebKey
    {
        [JsonPropertyName("kty")] public string Kty { get; init; } = "EC";
        [JsonPropertyName("crv")] public string Crv { get; init; } = "P-256";
        [JsonPropertyName("d")] public string D { get; init; } = "";
        [JsonPropertyName("x")] public string X { get; init; } = "";
        [JsonPropertyName("y")] public string Y { get; init; } = "";
        [JsonPropertyName("ext")] public bool Ext { get; init; } = true;
        [JsonPropertyName("key_ops")] public string[] KeyOps { get; init; } = new[] { "deriveBits", "deriveKey" };
    }
}
