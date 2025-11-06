using DotnetWeatherBackend;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Extensions.Http;
using System.Threading.RateLimiting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("Logs/weatherlog.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

//HTTP logging for middle
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
    logging.RequestHeaders.Add("sec-ch-ua");
    logging.ResponseHeaders.Add("MyResponseHeader");
    logging.MediaTypeOptions.AddText("application/javascript");
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
    logging.CombineLogs = true;
});

//  Swagger 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//  Authorization 
builder.Services.AddAuthorization();

//  Memory cache + WeatherService + Config 
builder.Services.AddMemoryCache();
builder.Services.Configure<WeatherApiOptions>(
    builder.Configuration.GetSection("WeatherApiOptions"));
builder.Services.AddScoped<WeatherService>();

//  Polly policies for HttpClient Retry: up to 3 retries with exponential backoff (2s → 4s → 8s).
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => (int)msg.StatusCode == 429) // Too Many Requests
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

//  HttpClient with Polly policies 
builder.Services.AddHttpClient("WeatherClient")
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

//  Rate limiting 
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

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendApps", policy =>
    {
        policy.WithOrigins(
            builder.Configuration["Frontend:ReactOrigin"]!,
            builder.Configuration["Frontend:AngularOrigin"]!
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

builder.Services.AddOpenApi();
builder.Host.UseSerilog();
var app = builder.Build();

//  Development environment setup 
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

//  Middleware pipeline 
app.UseHttpsRedirection();
app.UseCors("AllowFrontendApps");
app.UseAuthorization();
app.UseRateLimiter();

//  Weather API Endpoint 
app.MapGet("/api/weather", async (
    string? city,
    double? lat,
    double? lon,
    WeatherService weatherService) =>
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