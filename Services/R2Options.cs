using System;
using Microsoft.Extensions.Configuration;

namespace Coflnet.StaticS3.Services;

public sealed class R2Options
{
    public string ServiceUrl { get; init; } = "";
    public string AuthenticationRegion { get; init; } = "auto";
    public string BucketName { get; init; } = "static";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";

    public static R2Options FromConfiguration(IConfiguration configuration)
    {
        var serviceUrl = configuration["R2:ServiceUrl"] ?? configuration["S3_HOST"] ?? "";
        if (!serviceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            serviceUrl = "https://" + serviceUrl;
        var options = new R2Options
        {
            ServiceUrl = serviceUrl,
            AuthenticationRegion = configuration["R2:AuthenticationRegion"] ?? "auto",
            BucketName = configuration["R2:BucketName"] ?? configuration["BUCKET_NAME"] ?? "static",
            AccessKey = configuration["R2:AccessKey"] ?? configuration["ACCESS_KEY"] ?? "",
            SecretKey = configuration["R2:SecretKey"] ?? configuration["SECRET_KEY"] ?? ""
        };
        if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("R2:ServiceUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            throw new InvalidOperationException("R2 credentials are required.");
        return options;
    }
}
