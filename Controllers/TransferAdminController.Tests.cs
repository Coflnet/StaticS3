using System;
using System.Threading.Tasks;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace StaticS3.Controllers;

[TestFixture, NonParallelizable]
public class TransferAdminControllerTests
{
    private static TransferOptions EnabledOptions() => new()
    {
        SigningKey = "01234567890123456789012345678901",
        AdminToken = "admin-token-1234",
        BucketName = "transfers",
        KeyPrefix = "transfers",
        MaxBytes = 1000,
        DefaultTtlSeconds = 3600,
        MaxTtlSeconds = 7200
    };

    private static TransferAdminController CreateController(TransferOptions options, IObjectStore store, DefaultHttpContext context, TransferTicketService ticketService) =>
        new(NullLogger<TransferAdminController>.Instance, store, options, ticketService)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

    private static int GetStatusCode(IActionResult result) => result switch
    {
        IStatusCodeActionResult statusCodeResult => statusCodeResult.StatusCode ?? StatusCodes.Status200OK,
        _ => throw new InvalidOperationException($"{result.GetType()} does not carry a status code.")
    };

    [Test]
    public void CreateLinkWithoutAdminTokenReturnsUnauthorized()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = controller.CreateLink(new CreateLinkRequest(null, null, null, null));

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task ListUploadsWithoutAdminTokenReturnsUnauthorized()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.ListUploads(null);

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task DownloadUploadWithoutAdminTokenReturnsUnauthorized()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.DownloadUpload("transfers/abc/file.bin");

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task AllEndpointsReturnNotFoundWhenDisabled()
    {
        var options = new TransferOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var createLinkResult = controller.CreateLink(new CreateLinkRequest(null, null, null, null));
        var listResult = await controller.ListUploads(null);
        var downloadResult = await controller.DownloadUpload("transfers/x");

        Assert.Multiple(() =>
        {
            Assert.That(GetStatusCode(createLinkResult), Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(GetStatusCode(listResult), Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(GetStatusCode(downloadResult), Is.EqualTo(StatusCodes.Status404NotFound));
        });
    }

    [Test]
    public void AdminRouteIsSeparateFromPublicTransferPrefix()
    {
        var adminRoute = (RouteAttribute)typeof(TransferAdminController).GetCustomAttributes(typeof(RouteAttribute), true)[0];
        var publicRoute = (RouteAttribute)typeof(TransferController).GetCustomAttributes(typeof(RouteAttribute), true)[0];

        Assert.Multiple(() =>
        {
            Assert.That(adminRoute.Template, Is.EqualTo("transfer-admin"),
                "Admin endpoints must not live under the public /transfer/ prefix that the Ingress exposes.");
            Assert.That(publicRoute.Template, Is.EqualTo("transfer"));
        });
    }
}
