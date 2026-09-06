using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
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
public class TransferControllerTests
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

    private static string ValidPublicKey()
    {
        var point = new byte[65];
        point[0] = 0x04;
        for (var i = 1; i < point.Length; i++)
            point[i] = (byte)i;
        return Convert.ToBase64String(point);
    }

    private static TransferController CreateController(TransferOptions options, IObjectStore store, DefaultHttpContext context, TransferTicketService ticketService) =>
        new(NullLogger<TransferController>.Instance, store, options, ticketService)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

    private static int GetStatusCode(IActionResult result) => result switch
    {
        IStatusCodeActionResult statusCodeResult => statusCodeResult.StatusCode ?? StatusCodes.Status200OK,
        _ => throw new InvalidOperationException($"{result.GetType()} does not carry a status code.")
    };

    [Test]
    public async Task UploadWithMissingTokenReturnsUnauthorizedAndDoesNotStore()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(new byte[] { 1, 2, 3 });
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.Upload();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status401Unauthorized));
        store.Verify(s => s.PutPrivateAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadWithExpiredTokenReturnsGone()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var ticket = ticketService.Create(60, null, null, ValidPublicKey());
        var expired = ticket with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var token = ticketService.Encode(expired);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Upload-Token"] = token;
        context.Request.Body = new MemoryStream(new byte[] { 1, 2, 3 });
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.Upload();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status410Gone));
        store.Verify(s => s.PutPrivateAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadExceedingTicketMaxBytesReturnsPayloadTooLarge()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var ticket = ticketService.Create(60, 10, null, ValidPublicKey());
        var token = ticketService.Encode(ticket);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Upload-Token"] = token;
        var body = new byte[1000];
        RandomNumberGenerator.Fill(body);
        context.Request.Body = new MemoryStream(body);
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.Upload();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        store.Verify(s => s.PutPrivateAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SuccessfulUploadReturnsCreatedAndStoresInTransferBucket()
    {
        var options = EnabledOptions();
        var ticketService = new TransferTicketService(options);
        var ticket = ticketService.Create(60, null, "My File", ValidPublicKey());
        var token = ticketService.Encode(ticket);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Upload-Token"] = token;
        var body = new byte[] { 1, 2, 3, 4, 5 };
        context.Request.Body = new MemoryStream(body);

        string? capturedKey = null;
        string? capturedBucketName = null;
        IReadOnlyDictionary<string, string>? capturedMetadata = null;
        var store = new Mock<IObjectStore>();
        store.Setup(s => s.PutPrivateAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string, IReadOnlyDictionary<string, string>, CancellationToken>((key, _, bucketName, metadata, _) =>
            {
                capturedKey = key;
                capturedBucketName = bucketName;
                capturedMetadata = metadata;
            })
            .ReturnsAsync(new UploadResult("ignored", UploadAction.Created, "hash", body.Length));

        var controller = CreateController(options, store.Object, context, ticketService);

        var result = await controller.Upload();

        Assert.Multiple(() =>
        {
            Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(capturedKey, Does.StartWith($"transfers/{ticket.Id}/"));
            Assert.That(capturedBucketName, Is.EqualTo("transfers"));
            Assert.That(capturedMetadata, Is.Not.Null);
            Assert.That(capturedMetadata!["ticket"], Is.EqualTo(ticket.Id));
        });
    }

    [Test]
    public async Task AllEndpointsReturnNotFoundWhenDisabled()
    {
        var options = new TransferOptions();
        var ticketService = new TransferTicketService(options);
        var context = new DefaultHttpContext();
        var store = new Mock<IObjectStore>();
        var controller = CreateController(options, store.Object, context, ticketService);

        var getLinkResult = controller.GetLink();
        var uploadResult = await controller.Upload();

        Assert.Multiple(() =>
        {
            Assert.That(GetStatusCode(getLinkResult), Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(GetStatusCode(uploadResult), Is.EqualTo(StatusCodes.Status404NotFound));
        });
    }

}
