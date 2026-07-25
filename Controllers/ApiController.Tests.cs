using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Http;
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
        var controller = CreateController(store.Object);
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
        var controller = CreateController(store.Object);
        var failures = UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Value;

        var result = await controller.Put("sky/icons/TEST");

        Assert.Multiple(() =>
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Value, Is.EqualTo(failures + 1));
        });
    }

    private static ApiController CreateController(IObjectStore store) =>
        new(NullLogger<ApiController>.Instance, store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
