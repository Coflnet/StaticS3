using System;
using Microsoft.Extensions.Configuration;

namespace Coflnet.StaticS3.Services;

public sealed class TransferOptions
{
    public string SigningKey { get; init; } = "";
    public string AdminToken { get; init; } = "";
    public string BucketName { get; init; } = "";
    public string KeyPrefix { get; init; } = "transfers";
    public long MaxBytes { get; init; } = 268435456;
    public int DefaultTtlSeconds { get; init; } = 172800;
    public int MaxTtlSeconds { get; init; } = 1209600;
    public string? PublicBaseUrl { get; init; }

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(SigningKey)
        && !string.IsNullOrWhiteSpace(AdminToken)
        && !string.IsNullOrWhiteSpace(BucketName);

    public static TransferOptions FromConfiguration(IConfiguration configuration, string staticBucketName)
    {
        var options = new TransferOptions
        {
            SigningKey = Setting(configuration, "Transfer:SigningKey", "TRANSFER_SIGNING_KEY") ?? "",
            AdminToken = Setting(configuration, "Transfer:AdminToken", "TRANSFER_ADMIN_TOKEN") ?? "",
            BucketName = Setting(configuration, "Transfer:BucketName", "TRANSFER_BUCKET_NAME") ?? "",
            KeyPrefix = Setting(configuration, "Transfer:KeyPrefix", "TRANSFER_KEY_PREFIX") ?? "transfers",
            MaxBytes = long.TryParse(Setting(configuration, "Transfer:MaxBytes", "TRANSFER_MAX_BYTES"), out var maxBytes) ? maxBytes : 268435456,
            DefaultTtlSeconds = int.TryParse(Setting(configuration, "Transfer:DefaultTtlSeconds", "TRANSFER_DEFAULT_TTL_SECONDS"), out var defaultTtl) ? defaultTtl : 172800,
            MaxTtlSeconds = int.TryParse(Setting(configuration, "Transfer:MaxTtlSeconds", "TRANSFER_MAX_TTL_SECONDS"), out var maxTtl) ? maxTtl : 1209600,
            PublicBaseUrl = Setting(configuration, "Transfer:PublicBaseUrl", "TRANSFER_PUBLIC_BASE_URL")
        };
        if (!options.Enabled)
            return options;
        if (string.Equals(options.BucketName, staticBucketName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Transfer:BucketName must differ from the public static bucket.");
        if (options.SigningKey.Length < 32)
            throw new InvalidOperationException("Transfer:SigningKey must be at least 32 characters.");
        if (options.AdminToken.Length < 16)
            throw new InvalidOperationException("Transfer:AdminToken must be at least 16 characters.");
        return options;
    }

    // appsettings.json ships the keys as empty strings, so a plain ?? would shadow the
    // environment fallback with "" and silently leave the feature disabled.
    private static string? Setting(IConfiguration configuration, string key, string environmentKey)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        value = configuration[environmentKey];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
