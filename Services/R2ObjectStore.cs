using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Coflnet.StaticS3.Services;

public static class UploadAction
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Unchanged = "unchanged";
    public const string Failed = "failed";
}

public sealed record UploadResult(string Key, string Action, string Sha256, long Bytes);

public interface IObjectStore
{
    Task<UploadResult> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
}

public sealed class R2ObjectStore : IObjectStore
{
    public const string CacheControl = "public, max-age=2592000, must-revalidate";
    private const string HashMetadata = "sha256";
    private readonly IAmazonS3 client;
    private readonly R2Options options;
    private readonly ILogger<R2ObjectStore> logger;

    public R2ObjectStore(IAmazonS3 client, R2Options options, ILogger<R2ObjectStore> logger)
    {
        this.client = client;
        this.options = options;
        this.logger = logger;
    }

    public async Task<UploadResult> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.Length;
        var hash = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)bytes))).ToLowerInvariant();
        var action = UploadAction.Updated;
        try
        {
            var metadata = await client.GetObjectMetadataAsync(new()
            {
                BucketName = options.BucketName,
                Key = key
            }, cancellationToken);
            var existingHash = metadata.Metadata.Keys
                .FirstOrDefault(candidate => candidate.EndsWith(HashMetadata, StringComparison.OrdinalIgnoreCase));
            if (existingHash != null && string.Equals(metadata.Metadata[existingHash], hash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Object {Key} is {Action} ({Sha256}, {Bytes} bytes)", key, UploadAction.Unchanged, hash, bytes);
                return new(key, UploadAction.Unchanged, hash, bytes);
            }
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == HttpStatusCode.NotFound
            || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            action = UploadAction.Created;
        }

        buffer.Position = 0;
        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = key,
            InputStream = buffer,
            ContentType = contentType,
            DisablePayloadSigning = true
        };
        request.Headers.CacheControl = CacheControl;
        request.Metadata[HashMetadata] = hash;
        await client.PutObjectAsync(request, cancellationToken);
        logger.LogInformation("Object {Key} was {Action} ({Sha256}, {Bytes} bytes)", key, action, hash, bytes);
        return new(key, action, hash, bytes);
    }
}

public static class UploadMetrics
{
    public static readonly Counter Uploads = Prometheus.Metrics.CreateCounter(
        "statics3_uploads_total",
        "StaticS3 upload attempts by result.",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    public static readonly Counter UploadedBytes = Prometheus.Metrics.CreateCounter(
        "statics3_uploaded_bytes_total",
        "Bytes written to object storage.");

    public static void Initialize()
    {
        foreach (var result in new[] { UploadAction.Created, UploadAction.Updated, UploadAction.Unchanged, UploadAction.Failed })
            Uploads.WithLabels(result).Inc(0);
        UploadedBytes.Inc(0);
    }
}
