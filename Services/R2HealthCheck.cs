using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Coflnet.StaticS3.Services;

public sealed class R2HealthCheck : IHealthCheck
{
    private readonly IAmazonS3 client;
    private readonly R2Options options;

    public R2HealthCheck(IAmazonS3 client, R2Options options)
    {
        this.client = client;
        this.options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.ListObjectsV2Async(new()
            {
                BucketName = options.BucketName,
                MaxKeys = 1
            }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("R2 is unavailable.", exception);
        }
    }
}
