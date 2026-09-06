using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace StaticS3.Controllers;

[ApiController]
[Route("transfer")]
public class TransferController : ControllerBase
{
    private readonly ILogger<TransferController> logger;
    private readonly IObjectStore objectStore;
    private readonly TransferOptions options;
    private readonly TransferTicketService ticketService;

    public TransferController(ILogger<TransferController> logger, IObjectStore objectStore, TransferOptions options, TransferTicketService ticketService)
    {
        this.logger = logger;
        this.objectStore = objectStore;
        this.options = options;
        this.ticketService = ticketService;
    }

    [HttpGet("link")]
    public IActionResult GetLink()
    {
        if (!options.Enabled)
            return NotFound();
        if (!TryResolveTicket(out var ticket, out var errorResult))
            return errorResult;

        return Ok(new
        {
            id = ticket.Id,
            label = ticket.Label,
            expiresAt = ticket.ExpiresAt,
            maxBytes = ticket.MaxBytes,
            publicKey = ticket.PublicKey,
            chunkSize = 4194304
        });
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload()
    {
        if (!options.Enabled)
            return NotFound();
        if (!TryResolveTicket(out var ticket, out var errorResult))
        {
            UploadMetrics.TransferUploads.WithLabels("rejected").Inc();
            return errorResult;
        }
        if (Request.ContentLength is { } contentLength && contentLength > ticket.MaxBytes)
        {
            UploadMetrics.TransferUploads.WithLabels("rejected").Inc();
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "Upload exceeds the ticket's maximum size." });
        }

        try
        {
            var tempPath = Path.GetTempFileName();
            await using var tempFile = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;
            try
            {
                int read;
                while ((read = await Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), HttpContext.RequestAborted)) > 0)
                {
                    total += read;
                    if (total > ticket.MaxBytes)
                    {
                        UploadMetrics.TransferUploads.WithLabels("rejected").Inc();
                        return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "Upload exceeds the ticket's maximum size." });
                    }
                    await tempFile.WriteAsync(buffer.AsMemory(0, read), HttpContext.RequestAborted);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (total == 0)
                return BadRequest(new { error = "The upload body was empty." });

            tempFile.Position = 0;
            var receivedAt = DateTimeOffset.UtcNow;
            var key = $"{options.KeyPrefix.Trim('/')}/{ticket.Id}/{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{RandomSuffix()}.bin";
            var metadata = new Dictionary<string, string>
            {
                ["ticket"] = ticket.Id,
                ["received"] = receivedAt.ToString("O")
            };
            var result = await objectStore.PutPrivateAsync(
                key,
                tempFile,
                options.BucketName,
                metadata,
                HttpContext.RequestAborted);

            UploadMetrics.TransferUploads.WithLabels("stored").Inc();
            return StatusCode(StatusCodes.Status201Created, new
            {
                key = result.Key,
                sha256 = result.Sha256,
                bytes = result.Bytes,
                receivedAt
            });
        }
        catch (Exception exception)
        {
            UploadMetrics.TransferUploads.WithLabels("failed").Inc();
            logger.LogError(exception, "Failed to store transfer upload for ticket {Id}", ticket.Id);
            return Problem("The upload could not be stored.");
        }
    }

    private bool TryResolveTicket(out UploadTicket ticket, out IActionResult errorResult)
    {
        var token = Request.Headers["X-Upload-Token"].ToString();
        if (!ticketService.TryDecode(token, out var decoded, out var error))
        {
            errorResult = error == "expired"
                ? StatusCode(StatusCodes.Status410Gone, new { error })
                : Unauthorized(new { error });
            ticket = null!;
            return false;
        }
        ticket = decoded!;
        errorResult = null!;
        return true;
    }

    private static string RandomSuffix() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(4)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
