using System;
using System.Collections.Generic;
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

public sealed record StoredObject(string Key, long Size, DateTimeOffset LastModified);

public interface IObjectStore
{
    Task<UploadResult> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<UploadResult> PutPrivateAsync(string key, Stream content, string bucketName, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, string bucketName, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string key, string bucketName, CancellationToken cancellationToken);
}

public sealed class R2ObjectStore : IObjectStore
{
    public const string CacheControl = "public, max-age=2592000, must-revalidate";
    public const string PrivateCacheControl = "private, no-store";
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

    public async Task<UploadResult> PutPrivateAsync(string key, Stream content, string bucketName, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            throw new ArgumentException("A seekable stream is required.", nameof(content));

        content.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant();
        var bytes = content.Length;
        content.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = "application/octet-stream",
            DisablePayloadSigning = true
        };
        request.Headers.CacheControl = PrivateCacheControl;
        request.Metadata[HashMetadata] = hash;
        foreach (var pair in metadata)
            request.Metadata[pair.Key] = pair.Value;
        await client.PutObjectAsync(request, cancellationToken);
        logger.LogInformation("Object {Key} was {Action} ({Sha256}, {Bytes} bytes)", key, UploadAction.Created, hash, bytes);
        return new(key, UploadAction.Created, hash, bytes);
    }

    public async Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, string bucketName, CancellationToken cancellationToken)
    {
        var results = new List<StoredObject>();
        string? continuationToken = null;
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, cancellationToken);
            // An empty listing comes back with S3Objects null rather than an empty list.
            results.AddRange((response.S3Objects ?? []).Select(item => new StoredObject(
                item.Key,
                item.Size ?? 0,
                item.LastModified.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(item.LastModified.Value, DateTimeKind.Utc)) : DateTimeOffset.MinValue)));
            continuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
        return results;
    }

    public async Task<Stream?> GetAsync(string key, string bucketName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            }, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == HttpStatusCode.NotFound
            || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
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

    public static readonly Counter TransferUploads = Prometheus.Metrics.CreateCounter(
        "statics3_transfer_uploads_total",
        "Secure file transfer upload attempts by result.",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    public static readonly Counter TransferLinks = Prometheus.Metrics.CreateCounter(
        "statics3_transfer_links_total",
        "Secure file transfer upload links created.");

    public static void Initialize()
    {
        foreach (var result in new[] { UploadAction.Created, UploadAction.Updated, UploadAction.Unchanged, UploadAction.Failed })
            Uploads.WithLabels(result).Inc(0);
        UploadedBytes.Inc(0);
        foreach (var result in new[] { "stored", "rejected", "failed" })
            TransferUploads.WithLabels(result).Inc(0);
        TransferLinks.Inc(0);
    }
}
