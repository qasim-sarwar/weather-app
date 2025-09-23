using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace DotnetWeatherBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddAuthorization();

            // Add rate limiting middleware
            // If a client exceeds any one (minute/hour/day) limit, they’ll get HTTP 503 Service Unavailable
            // By default, limits apply per client IP(RemoteIpAddress).
            // Throttling: queues or delays excess requests instead of blocking is configured with QueueLimit > 0
            builder.Services.AddRateLimiter(options =>
            {
                // per minute < 600 requests
                options.AddFixedWindowLimiter("PerMinute", opt =>
                {
                    opt.PermitLimit = 600;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                // per hour < 5,000 requests
                options.AddFixedWindowLimiter("PerHour", opt =>
                {
                    opt.PermitLimit = 5000;
                    opt.Window = TimeSpan.FromHours(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                // per day < 10,000 requests
                options.AddFixedWindowLimiter("PerDay", opt =>
                {
                    opt.PermitLimit = 10000;
                    opt.Window = TimeSpan.FromDays(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });
            });

            builder.Services.AddHttpClient();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAngular", policy =>
                    policy.WithOrigins("http://localhost:4200")
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            builder.Services.AddOpenApi();
            builder.Services.AddScoped<WeatherService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAngular");
            app.UseAuthorization();

            // Enable global rate limiting
            app.UseRateLimiter();

            // Match Angular service params (lat, lon, city)
            app.MapGet("/api/weather", async (string? city, double? lat, double? lon, WeatherService weatherService) =>
            {
                var (result, statusCode) = await weatherService.GetWeatherAsync(city, lat, lon);

                return statusCode switch
                {
                    200 => Results.Ok(result),
                    400 => Results.BadRequest(result),
                    404 => Results.NotFound(result),
                    _ => Results.Problem(result?.ToString())
                };
            })
            .WithName("GetWeather")
            // Apply rate limiting policies here
            .RequireRateLimiting("PerMinute")
            .RequireRateLimiting("PerHour")
            .RequireRateLimiting("PerDay");

            app.Run();
        }
    }
}
