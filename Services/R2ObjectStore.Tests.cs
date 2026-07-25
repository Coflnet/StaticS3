using System.IO;
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
            .Callback<PutObjectRequest, CancellationToken>((value, _) => request = value)
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

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));

    private static string Hash(string value) =>
        System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
