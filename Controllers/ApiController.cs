using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Amazon.S3;

namespace StaticS3.Controllers
{
    [ApiController]
    [Route("{*url}")]
    public class ApiController : ControllerBase
    {
        private readonly ILogger<ApiController> _logger;
        private string bucketName;
        private AmazonS3Client s3Client;

        public ApiController(ILogger<ApiController> logger, IConfiguration config)
        {
            _logger = logger;
            // minioClient = new MinioClient().WithEndpoint(config["S3_HOST"]).WithCredentials(config["ACCESS_KEY"], config["SECRET_KEY"]).Build();

            AmazonS3Config awsCofig = new AmazonS3Config();
            awsCofig.ServiceURL = "https://" + config["S3_HOST"];
            // use path style access
            awsCofig.ForcePathStyle = true;

            s3Client = new AmazonS3Client(
                    config["ACCESS_KEY"],
                    config["SECRET_KEY"],
                    awsCofig
                    );
            bucketName = config["BUCKET_NAME"];

            _logger.LogInformation($"Using bucket {bucketName}, s3 host {config["S3_HOST"]}, access key {config["ACCESS_KEY"]}, secret key {config["SECRET_KEY"].Length}");
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var route = Request.Path.Value.TrimStart('/');
            var response = new MemoryStream();
            _logger.LogInformation($"Getting {route} from S3 bucket {bucketName}");
            if (route == "favicon.ico")
            {
                return StatusCode(200);
            }
            try
            {
                using var data = await s3Client.GetObjectAsync(bucketName, route);
                _logger.LogInformation($"Got {route} from S3 bucket {bucketName} with {data.ContentLength} bytes");
                await data.ResponseStream.CopyToAsync(response);
            }
            catch (Minio.Exceptions.ObjectNotFoundException)
            {
                return StatusCode(404);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting object");
                return StatusCode(500);
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
