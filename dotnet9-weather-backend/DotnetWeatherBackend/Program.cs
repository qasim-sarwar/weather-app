using Microsoft.AspNetCore.RateLimiting;
using System.Runtime.ConstrainedExecution;
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
            app.MapGet("/api/weather", async (string? city, double? lat, double? lon, IHttpClientFactory httpClientFactory) =>
            {
                var client = httpClientFactory.CreateClient();
                string url;

                if (!string.IsNullOrEmpty(city))
                {
                    var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1";
                    var geo = await client.GetFromJsonAsync<JsonElement>(geoUrl);

                    if (!geo.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    {
                        return Results.NotFound(new { error = "City not found" });
                    }

                    lat = results[0].GetProperty("latitude").GetDouble();
                    lon = results[0].GetProperty("longitude").GetDouble();
                }

                if (lat == null || lon == null)
                {
                    return Results.BadRequest(new { error = "Either city or lat/lon must be provided" });
                }

                url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";

                try
                {
                    var response = await client.GetFromJsonAsync<object>(url);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to fetch weather: {ex.Message}");
                }
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
