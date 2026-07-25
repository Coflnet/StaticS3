using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
    [Consumes("image/png")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Put(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Split('/').Contains(".."))
            return BadRequest(new { error = "Invalid object key." });
        try
        {
            var result = await objectStore.PutAsync(
                key,
                Request.Body,
                "image/png",
                HttpContext.RequestAborted);
            UploadMetrics.Uploads.WithLabels(result.Action).Inc();
            if (result.Action != UploadAction.Unchanged)
                UploadMetrics.UploadedBytes.Inc(result.Bytes);
            return StatusCode(result.Action == UploadAction.Created ? 201 : 200, result);
        }
        catch (Exception exception)
        {
            UploadMetrics.Uploads.WithLabels(UploadAction.Failed).Inc();
            _logger.LogError(exception, "Failed to store {Key}", key);
            return Problem("The object could not be stored.");
        }
    }
}
