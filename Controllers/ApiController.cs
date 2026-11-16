using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Coflnet.StaticS3.Services;

namespace StaticS3.Controllers;
[ApiController]
[Route("objects/{**key}")]
public class ApiController : ControllerBase
{
    private readonly ILogger<ApiController> _logger;
    private readonly IObjectStore objectStore;

    public ApiController(ILogger<ApiController> logger, IObjectStore objectStore)
    {
        _logger = logger;
        this.objectStore = objectStore;
    }

    [HttpPut]
    [Consumes("image/png", "image/webp", "image/avif", "image/jpeg", "video/mp4", "video/webm")]
    // Allows the largest type; the per-type limit is enforced in Put because the attribute is static.
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> Put(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Split('/').Contains(".."))
            return BadRequest(new { error = "Invalid object key." });
        var contentType = GetMediaType();
        if (!UploadPolicy.IsSupported(contentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { error = "Unsupported content type." });
        if (!UploadPolicy.ExtensionMatches(contentType!, key))
            return BadRequest(new { error = $"The key extension does not match {contentType}." });
        var limit = UploadPolicy.MaxBytesFor(contentType!);
        if (Request.ContentLength > limit)
            return TooLarge(limit);
        LimitBody(limit);
        try
        {
            var result = await objectStore.PutAsync(
                key,
                Request.Body,
                contentType!.ToLowerInvariant(),
                HttpContext.RequestAborted);
            UploadMetrics.Uploads.WithLabels(result.Action).Inc();
            if (result.Action != UploadAction.Unchanged)
                UploadMetrics.UploadedBytes.Inc(result.Bytes);
            return StatusCode(result.Action == UploadAction.Created ? 201 : 200, result);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(limit);
        }
        catch (Exception exception)
        {
            UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Inc();
            _logger.LogError(exception, "Failed to store {Key}", key);
            return Problem("The object could not be stored.");
        }
    }

    private string? GetMediaType() =>
        MediaTypeHeaderValue.TryParse(Request.ContentType, out var header) ? header.MediaType.Value : null;

    // Covers chunked uploads without a Content-Length: Kestrel then aborts the read past the limit.
    private void LimitBody(long limit)
    {
        var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = limit;
    }

    private ObjectResult TooLarge(long limit) =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"The upload exceeds {limit} bytes." });
}
