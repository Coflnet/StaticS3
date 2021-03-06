using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;

namespace StaticS3.Controllers
{
    [ApiController]
    [Route("{*url}")]
    public class ApiController : ControllerBase
    {
        private readonly ILogger<ApiController> _logger;
        private MinioClient minioClient;

        private string bucketName;

        public ApiController(ILogger<ApiController> logger, IConfiguration config)
        {
            _logger = logger;
            minioClient = new MinioClient(config["S3_HOST"],
                                       config["ACCESS_KEY"],
                                       config["SECRET_KEY"]
                                 );//.WithSSL();
            bucketName = config["BUCKET_NAME"];
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var route = Request.Path.Value.TrimStart('/');
            var response = new MemoryStream();
            Console.WriteLine(route);
            try
            {
                await minioClient.GetObjectAsync(bucketName, route, cb =>
            {
                cb.CopyTo(response);
            });
            } catch(Minio.Exceptions.ObjectNotFoundException)
            {
                return StatusCode(404);
            }



            response.Position = 0;
            new FileExtensionContentTypeProvider().TryGetContentType(Path.GetFileName(route), out string contentType);

            return new FileStreamResult(response, contentType)
            {
                EnableRangeProcessing = true
            };
        }
    }
}
