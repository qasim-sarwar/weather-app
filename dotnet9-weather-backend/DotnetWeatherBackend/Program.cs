using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Extensions.Http;
using System.Threading.RateLimiting;

namespace DotnetWeatherBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add Swagger support
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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

            // Add HttpClient with Polly policies
            builder.Services.AddHttpClient("WeatherClient")
                .AddPolicyHandler(GetRetryPolicy())
                .AddPolicyHandler(GetCircuitBreakerPolicy());

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
                app.UseSwagger();
                app.UseSwaggerUI();
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

        // Retry: up to 3 retries with exponential backoff (2s → 4s → 8s).
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                    });
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, breakDelay) =>
                    {
                        Console.WriteLine($"Circuit opened for {breakDelay.TotalSeconds}s: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                    },
                    onReset: () => Console.WriteLine("Circuit closed, requests flowing again."),
                    onHalfOpen: () => Console.WriteLine("Circuit half-open, testing the waters.")
                );
        }
    }
}
