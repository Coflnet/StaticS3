using System;
using System.Collections.Generic;
using Amazon.Runtime;
using Amazon.S3;
using Coflnet.StaticS3.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Prometheus;

namespace StaticS3
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            UploadMetrics.Initialize();
            var options = R2Options.FromConfiguration(Configuration);
            services.AddSingleton(options);
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = options.ServiceUrl,
                    AuthenticationRegion = options.AuthenticationRegion,
                    ForcePathStyle = true,
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                }));
            services.AddSingleton<IObjectStore, R2ObjectStore>();
            services.AddHealthChecks()
                .AddCheck<R2HealthCheck>("r2", tags: new[] { "ready" });

            var transferOptions = TransferOptions.FromConfiguration(Configuration, options.BucketName);
            services.AddSingleton(transferOptions);
            services.AddSingleton<TransferTicketService>();

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "StaticS3", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "StaticS3 v1"));
            }

            var transferOptions = app.ApplicationServices.GetRequiredService<TransferOptions>();
            if (!transferOptions.Enabled)
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(transferOptions.SigningKey)) missing.Add("Transfer:SigningKey");
                if (string.IsNullOrWhiteSpace(transferOptions.AdminToken)) missing.Add("Transfer:AdminToken");
                if (string.IsNullOrWhiteSpace(transferOptions.BucketName)) missing.Add("Transfer:BucketName");
                logger.LogWarning("Secure file transfer feature is disabled, missing configuration: {Missing}", string.Join(", ", missing));
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/live", new()
                {
                    Predicate = _ => false
                });
                endpoints.MapHealthChecks("/health/ready", new()
                {
                    Predicate = registration => registration.Tags.Contains("ready")
                });
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
