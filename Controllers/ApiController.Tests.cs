using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace StaticS3.Controllers;

[TestFixture, NonParallelizable]
public class ApiControllerTests
{
    [Test]
    public async Task CreatedResponseUpdatesObjectAndByteMetrics()
    {
        var store = new Mock<IObjectStore>();
        store.Setup(value => value.PutAsync("sky/icons/TEST", It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult("sky/icons/TEST", UploadAction.Created, "hash", 4));
        var controller = CreateController(store.Object, "image/png");
        var uploads = UploadMetrics.Uploads.WithLabels(UploadAction.Created).Value;
        var bytes = UploadMetrics.UploadedBytes.Value;

        var result = await controller.Put("sky/icons/TEST");

        Assert.Multiple(() =>
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(UploadMetrics.Uploads.WithLabels(UploadAction.Created).Value, Is.EqualTo(uploads + 1));
            Assert.That(UploadMetrics.UploadedBytes.Value, Is.EqualTo(bytes + 4));
        });
    }

    [Test]
    public async Task FailureReturnsProblemAndUpdatesFailureMetric()
    {
        var store = new Mock<IObjectStore>();
        store.Setup(value => value.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("failed"));
        var controller = CreateController(store.Object, "image/png");
        var failures = UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Value;

        var result = await controller.Put("sky/icons/TEST");

        Assert.Multiple(() =>
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Value, Is.EqualTo(failures + 1));
        });
    }

    [TestCase("image/png", "sky/icons/TEST")]
    [TestCase("image/webp", "clips/a.webp")]
    [TestCase("image/avif", "clips/a.avif")]
    [TestCase("image/jpeg", "clips/a.jpg")]
    [TestCase("image/jpeg", "clips/a.JPEG")]
    [TestCase("image/gif", "sky/icons/TEST.gif")]
    [TestCase("video/mp4", "clips/a.mp4")]
    [TestCase("video/webm", "clips/a.webm")]
    public async Task StoresObjectWithItsOwnContentType(string contentType, string key)
    {
        var store = StoreReturning(key, UploadAction.Created);
        var controller = CreateController(store.Object, contentType);

        var result = await controller.Put(key);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        store.Verify(value => value.PutAsync(key, It.IsAny<Stream>(), contentType, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ContentTypeParametersAreIgnored()
    {
        var store = StoreReturning("clips/a.mp4", UploadAction.Created);
        var controller = CreateController(store.Object, "Video/MP4; foo=bar");

        var result = await controller.Put("clips/a.mp4");

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        store.Verify(value => value.PutAsync("clips/a.mp4", It.IsAny<Stream>(), "video/mp4", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("image/svg+xml")]
    [TestCase("application/octet-stream")]
    [TestCase("")]
    public async Task UnsupportedContentTypeIsRejectedWith415(string contentType)
    {
        var store = new Mock<IObjectStore>();
        var controller = CreateController(store.Object, contentType);

        var result = await controller.Put("clips/a.mp4");

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status415UnsupportedMediaType));
        store.VerifyNoOtherCalls();
    }

    [TestCase("video/mp4", "clips/a.webm")]
    [TestCase("video/webm", "clips/a.mp4")]
    [TestCase("video/mp4", "clips/a.png")]
    [TestCase("image/webp", "clips/a.mp4")]
    [TestCase("image/avif", "clips/a")]
    [TestCase("image/jpeg", "clips/a.png")]
    [TestCase("image/gif", "sky/icons/TEST")]
    public async Task ExtensionMismatchForNewTypesIsRejectedWith400(string contentType, string key)
    {
        var store = new Mock<IObjectStore>();
        var controller = CreateController(store.Object, contentType);

        var result = await controller.Put(key);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        store.VerifyNoOtherCalls();
    }

    [TestCase("sky/icons/TEST")]
    [TestCase("sky/icons/TEST.bin")]
    public async Task PngIsAcceptedWithoutPngExtension(string key)
    {
        var store = StoreReturning(key, UploadAction.Created);
        var controller = CreateController(store.Object, "image/png");

        var result = await controller.Put(key);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
    }

    [TestCase("image/png", "a", UploadPolicy.ImageMaxBytes, true)]
    [TestCase("image/png", "a", UploadPolicy.ImageMaxBytes + 1, false)]
    [TestCase("image/webp", "a.webp", UploadPolicy.ImageMaxBytes + 1, false)]
    [TestCase("image/gif", "a.gif", UploadPolicy.ImageMaxBytes, true)]
    [TestCase("image/gif", "a.gif", UploadPolicy.ImageMaxBytes + 1, false)]
    [TestCase("video/mp4", "a.mp4", UploadPolicy.VideoMaxBytes, true)]
    [TestCase("video/mp4", "a.mp4", UploadPolicy.ImageMaxBytes + 1, true)]
    [TestCase("video/webm", "a.webm", UploadPolicy.VideoMaxBytes + 1, false)]
    public async Task EnforcesPerTypeSizeLimit(string contentType, string key, long length, bool accepted)
    {
        var store = StoreReturning(key, UploadAction.Created);
        var controller = CreateController(store.Object, contentType);
        controller.Request.ContentLength = length;

        var result = await controller.Put(key);

        var expected = accepted ? StatusCodes.Status201Created : StatusCodes.Status413PayloadTooLarge;
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task ChunkedUploadsGetThePerTypeLimitOnTheRequestFeature()
    {
        var store = StoreReturning("a.png", UploadAction.Created);
        var controller = CreateController(store.Object, "image/png");
        var feature = new TestBodySizeFeature();
        controller.HttpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        await controller.Put("a.png");

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(UploadPolicy.ImageMaxBytes));
    }

    [Test]
    public async Task OversizedChunkedUploadReturns413()
    {
        var store = new Mock<IObjectStore>();
        store.Setup(value => value.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge));
        var controller = CreateController(store.Object, "video/mp4");

        var result = await controller.Put("a.mp4");

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
    }

    [Test]
    public async Task UnchangedReuploadReportsUnchangedWithoutCountingBytes()
    {
        var store = StoreReturning("a.mp4", UploadAction.Unchanged);
        var controller = CreateController(store.Object, "video/mp4");
        var bytes = UploadMetrics.UploadedBytes.Value;

        var result = await controller.Put("a.mp4");

        Assert.Multiple(() =>
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(((UploadResult)((ObjectResult)result).Value!).Action, Is.EqualTo(UploadAction.Unchanged));
            Assert.That(UploadMetrics.UploadedBytes.Value, Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ConsumesAttributeListsExactlyThePolicyTypes()
    {
        var consumes = typeof(ApiController).GetMethod(nameof(ApiController.Put))!
            .GetCustomAttributes(typeof(ConsumesAttribute), false).Cast<ConsumesAttribute>().Single();

        Assert.That(consumes.ContentTypes, Is.EquivalentTo(UploadPolicy.ContentTypes));
    }

    [Test]
    public void ActionSizeLimitCoversTheLargestType()
    {
        var limit = typeof(ApiController).GetMethod(nameof(ApiController.Put))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), false).Cast<RequestSizeLimitAttribute>().Single();

        Assert.That(UploadPolicy.AbsoluteMaxBytes, Is.EqualTo(UploadPolicy.VideoMaxBytes));
        var bytes = typeof(RequestSizeLimitAttribute)
            .GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(limit);
        Assert.That(bytes, Is.EqualTo(UploadPolicy.AbsoluteMaxBytes));
    }

    private static Mock<IObjectStore> StoreReturning(string key, string action)
    {
        var store = new Mock<IObjectStore>();
        store.Setup(value => value.PutAsync(key, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult(key, action, "hash", 4));
        return store;
    }

    private sealed class TestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }

    private static ApiController CreateController(IObjectStore store, string contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        return new(NullLogger<ApiController>.Instance, store)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
