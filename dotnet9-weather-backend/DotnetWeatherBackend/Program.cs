using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace DotnetWeatherBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddAuthorization();

            // Register caching with options and WeatherService
            builder.Services.AddMemoryCache();
            builder.Services.Configure<WeatherApiOptions>(
                builder.Configuration.GetSection("WeatherApiOptions"));
            builder.Services.AddScoped<WeatherService>();

            // Add rate limiting middleware
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("PerMinute", opt =>
                {
                    opt.PermitLimit = 600;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("PerHour", opt =>
                {
                    opt.PermitLimit = 5000;
                    opt.Window = TimeSpan.FromHours(1);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

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
            .RequireRateLimiting("PerMinute")
            .RequireRateLimiting("PerHour")
            .RequireRateLimiting("PerDay");

            app.Run();
        }
    }
}