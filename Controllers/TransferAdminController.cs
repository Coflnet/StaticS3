using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace StaticS3.Controllers;

public sealed record CreateLinkRequest(int? TtlSeconds, long? MaxBytes, string? Label, string? PublicKey);

[ApiController]
[Route("transfer-admin")]
public class TransferAdminController : ControllerBase
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    private readonly ILogger<TransferAdminController> logger;
    private readonly IObjectStore objectStore;
    private readonly TransferOptions options;
    private readonly TransferTicketService ticketService;

    public TransferAdminController(ILogger<TransferAdminController> logger, IObjectStore objectStore, TransferOptions options, TransferTicketService ticketService)
    {
        this.logger = logger;
        this.objectStore = objectStore;
        this.options = options;
        this.ticketService = ticketService;
    }

    [HttpPost("links")]
    public IActionResult CreateLink([FromBody] CreateLinkRequest request)
    {
        if (!options.Enabled)
            return NotFound();
        if (!IsAdmin())
            return Unauthorized();

        var publicKey = request.PublicKey;
        string? privateKeyJwk = null;
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            var pair = TransferTicketService.GenerateKeyPair();
            publicKey = pair.PublicKey;
            privateKeyJwk = pair.PrivateKeyJwk;
        }

        UploadTicket ticket;
        try
        {
            ticket = ticketService.Create(request.TtlSeconds, request.MaxBytes, request.Label, publicKey);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }

        var token = ticketService.Encode(ticket);
        var baseUrl = options.PublicBaseUrl?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        UploadMetrics.TransferLinks.Inc();
        logger.LogInformation("Created transfer link {Id} expiring {ExpiresAt} maxBytes {MaxBytes} label {Label}",
            ticket.Id, ticket.ExpiresAt, ticket.MaxBytes, ticket.Label);

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = ticket.Id,
            token,
            url = $"{baseUrl}/transfer/#{token}",
            // decrypt.html moved to the cluster-internal /transfer-admin/ prefix, so a full URL built
            // from the public baseUrl would be wrong and unreachable - give a relative path instead,
            // valid only via the port-forwarded admin console.
            decryptPath = "/transfer-admin/decrypt.html",
            expiresAt = ticket.ExpiresAt,
            maxBytes = ticket.MaxBytes,
            label = ticket.Label,
            publicKey = ticket.PublicKey,
            privateKeyJwk,
            note = privateKeyJwk == null
                ? (string?)null
                : "The private key is shown only once here and is never logged or stored by the server - save it now."
        });
    }

    [HttpGet("uploads")]
    public async Task<IActionResult> ListUploads([FromQuery] string? id)
    {
        if (!options.Enabled)
            return NotFound();
        if (!IsAdmin())
            return Unauthorized();
        if (!string.IsNullOrEmpty(id) && !IdPattern.IsMatch(id))
            return BadRequest(new { error = "Invalid id." });

        var prefix = string.IsNullOrEmpty(id)
            ? $"{options.KeyPrefix.Trim('/')}/"
            : $"{options.KeyPrefix.Trim('/')}/{id}/";
        var items = await objectStore.ListAsync(prefix, options.BucketName, HttpContext.RequestAborted);
        return Ok(items.Select(item => new { key = item.Key, size = item.Size, lastModified = item.LastModified }));
    }

    [HttpGet("uploads/{**key}")]
    public async Task<IActionResult> DownloadUpload(string key)
    {
        if (!options.Enabled)
            return NotFound();
        if (!IsAdmin())
            return Unauthorized();

        var prefix = $"{options.KeyPrefix.Trim('/')}/";
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(prefix, StringComparison.Ordinal) || key.Split('/').Contains(".."))
            return BadRequest(new { error = "Invalid key." });

        var stream = await objectStore.GetAsync(key, options.BucketName, HttpContext.RequestAborted);
        if (stream == null)
            return NotFound();

        return File(stream, "application/octet-stream", fileDownloadName: key.Split('/').Last());
    }

    private bool IsAdmin()
    {
        var provided = Request.Headers["X-Admin-Token"].ToString();
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(options.AdminToken);
        if (providedBytes.Length != expectedBytes.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
