using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.StaticS3.Services;

[TestFixture]
public class R2ObjectStoreTests
{
    private static readonly R2Options Options = new()
    {
        BucketName = "static",
        ServiceUrl = "https://example.r2.cloudflarestorage.com",
        AccessKey = "access",
        SecretKey = "secret"
    };

    [Test]
    public async Task CreatesMissingObjectWithHashAndCacheMetadata()
    {
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        PutObjectRequest? request = null;
        client.Setup(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((value, _) =>
            {
                request = value;
                value.InputStream.Dispose();
            })
            .ReturnsAsync(new PutObjectResponse());
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);

        var result = await store.PutAsync("sky/icons/TEST", Bytes("icon"), "image/png", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(UploadAction.Created));
            Assert.That(result.Bytes, Is.EqualTo(4));
            Assert.That(result.Sha256, Is.EqualTo(Hash("icon")));
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.ContentType, Is.EqualTo("image/png"));
            Assert.That(request.Headers.CacheControl, Is.EqualTo(R2ObjectStore.CacheControl));
            Assert.That(request.Metadata["sha256"], Is.EqualTo(result.Sha256));
        });
    }

    [Test]
    public async Task DoesNotWriteUnchangedObject()
    {
        var hash = Hash("icon");
        var metadata = new GetObjectMetadataResponse();
        metadata.Metadata["sha256"] = hash;
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);

        var result = await store.PutAsync("sky/icons/TEST", Bytes("icon"), "image/png", CancellationToken.None);

        Assert.That(result.Action, Is.EqualTo(UploadAction.Unchanged));
        client.Verify(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ReplacesChangedObject()
    {
        var metadata = new GetObjectMetadataResponse();
        metadata.Metadata["sha256"] = Hash("old");
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        client.Setup(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);

        var result = await store.PutAsync("sky/icons/TEST", Bytes("new"), "image/png", CancellationToken.None);

        Assert.That(result.Action, Is.EqualTo(UploadAction.Updated));
        client.Verify(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PutPrivateAsyncWritesWithoutFetchingMetadataAndUsesPrivateCacheControl()
    {
        var client = new Mock<IAmazonS3>();
        PutObjectRequest? request = null;
        client.Setup(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(new PutObjectResponse());
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);
        var metadata = new Dictionary<string, string> { ["ticket"] = "abc123" };

        var result = await store.PutPrivateAsync("transfers/abc123/file.bin", Bytes("payload"), "transfers", metadata, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(UploadAction.Created));
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.BucketName, Is.EqualTo("transfers"));
            Assert.That(request.Headers.CacheControl, Is.EqualTo(R2ObjectStore.PrivateCacheControl));
            Assert.That(request.Metadata["ticket"], Is.EqualTo("abc123"));
            Assert.That(request.Metadata["sha256"], Is.EqualTo(result.Sha256));
        });
        client.Verify(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void PutPrivateAsyncThrowsForNonSeekableStream()
    {
        var client = new Mock<IAmazonS3>();
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);
        var metadata = new Dictionary<string, string>();

        Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutPrivateAsync("transfers/abc123/file.bin", new NonSeekableStream(Bytes("payload")), "transfers", metadata, CancellationToken.None));
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream inner;

        public NonSeekableStream(Stream inner) => this.inner = inner;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    [Test]
    public async Task ListAsyncReturnsEmptyWhenBucketHasNoMatchingObjects()
    {
        // The SDK reports an empty listing with a null S3Objects, which used to throw.
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = null, IsTruncated = false });
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);

        var result = await store.ListAsync("transfers/", "transfers", CancellationToken.None);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAsyncFollowsContinuationTokens()
    {
        var client = new Mock<IAmazonS3>();
        var responses = new Queue<ListObjectsV2Response>(new[]
        {
            new ListObjectsV2Response
            {
                S3Objects = new List<S3Object> { new() { Key = "transfers/a/1.bin", Size = 10, LastModified = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) } },
                IsTruncated = true,
                NextContinuationToken = "next"
            },
            new ListObjectsV2Response
            {
                S3Objects = new List<S3Object> { new() { Key = "transfers/a/2.bin", Size = 20, LastModified = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc) } },
                IsTruncated = false
            }
        });
        client.Setup(value => value.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses.Dequeue());
        var store = new R2ObjectStore(client.Object, Options, NullLogger<R2ObjectStore>.Instance);

        var result = await store.ListAsync("transfers/", "transfers", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.Key), Is.EqualTo(new[] { "transfers/a/1.bin", "transfers/a/2.bin" }));
            Assert.That(result[1].Size, Is.EqualTo(20));
        });
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));

    private static string Hash(string value) =>
        System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
