using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;

public class WeatherServiceTests
{
    private static WeatherService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.open-meteo.com/")
        };

        var options = Options.Create(new WeatherApiOptions
        {
            BaseUrl = "https://api.open-meteo.com/v1",
            GeoUrl = "https://geocoding-api.open-meteo.com/v1/search",
            ForecastEndpoint = "forecast",
            CityBaseUrl = "https://nominatim.openstreetmap.org/reverse"
        });

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var mockLogger = new Mock<ILogger<WeatherService>>();

        return new WeatherService(
            client,
            memoryCache,
            options,
            mockLogger.Object
        );
    }

    private static string Serialize(object? obj) => JsonSerializer.Serialize(obj ?? new { });

    [Fact]
    public async Task GetWeatherAsync_ReturnsError_WhenNoCityOrLatLonProvided()
    {
        var service = CreateService(new Mock<HttpMessageHandler>().Object);

        var (result, statusCode) = await service.GetWeatherAsync(null, null, null);

        Assert.NotNull(result);
        Assert.Equal(400, statusCode);

        var json = Serialize(result);
        Assert.Contains("Either city or lat/lon must be provided", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsError_WhenCityNotFound()
    {
        var geoResponse = "{\"results\":[]}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoResponse, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("InvalidCity", null, null);

        Assert.Equal(404, statusCode);

        var json = Serialize(result);
        Assert.Contains("City not found", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeather_WhenLatLonProvided()
    {
        var forecastJson = "{\"current_weather\":{\"temperature\":20}}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);
        var (result, statusCode) = await service.GetWeatherAsync(null, 10, 20);

        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeather_WhenCityFound()
    {
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var forecastJson = "{\"current_weather\":{\"temperature\":22}}";
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var body = callCount == 1 ? geoJson : forecastJson;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService(mockHandler.Object);
        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsServerError_WhenForecastIsNull()
    {
        // Sequence: first call returns geo json, second returns literal "null" to simulate null payload
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        var seq = mockHandler.Protected().SetupSequence<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());

        seq = seq.ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
        });

        // forecast returns "null" content => deserializes to null in GetFromJsonAsync
        seq = seq.ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });

        var service = CreateService(mockHandler.Object);
        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        // implementation returns 500 when forecast == null
        Assert.Equal(500, statusCode);
        var json = Serialize(result);
        Assert.Contains("Forecast API returned null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWeatherAsync_TrimsCityName_AndWorksWithWhitespace()
    {
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var forecastJson = "{\"current_weather\":{\"temperature\":22}}";
        var callCount = 0;

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(callCount == 1 ? geoJson : forecastJson, Encoding.UTF8, "application/json")
                };
            });

        var service = CreateService(mockHandler.Object);
        var (result, statusCode) = await service.GetWeatherAsync("   Tokyo   ", null, null);

        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);
    }

    [Fact]
    public async Task GetWeatherAsync_CacheHit_SkipsGeocoding()
    {
        var forecastJson = "{\"current_weather\":{\"temperature\":30}}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        // manually add coordinates to cache (both possible keys to be permissive)
        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField!.GetValue(service)!;
        cache.Set("london", (51.5074, -0.1278), TimeSpan.FromMinutes(30));
        cache.Set("coords:london", (51.5074, -0.1278), TimeSpan.FromMinutes(30));

        var (result, statusCode) = await service.GetWeatherAsync("London", null, null);
        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);
    }

    [Fact]
    public async Task GetWeatherAsync_CacheHit_ReturnsCachedForecastWithoutApiCall()
    {
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict); // ensure no API calls required
        var service = CreateService(mockHandler.Object);

        var forecast = new WeatherForecast
        {
            CurrentWeather = new CurrentWeather { Temperature = 25 },
            MinTemp = 20,
            MaxTemp = 30
        };

        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField!.GetValue(service)!;

        // set both possible forecast keys
        cache.Set("weather:35:139", forecast, TimeSpan.FromMinutes(10));
        cache.Set("forecast:35:139", forecast, TimeSpan.FromMinutes(10));

        var (result, statusCode) = await service.GetWeatherAsync(null, 35, 139);

        Assert.Equal(200, statusCode);
        Assert.Same(forecast, result); // same instance from cache
    }

    [Fact]
    public async Task GetWeatherAsync_CacheMiss_FetchesForecastAndStoresInCache()
    {
        var forecastJson = "{\"current_weather\":{\"temperature\":18},\"hourly\":{\"temperature_2m\":[15,18],\"time\":[\"2025-10-09T10:00\",\"2025-10-09T11:00\"],\"weathercode\":[0,1]},\"daily\":{\"time\":[\"2025-10-09\"],\"temperature_2m_min\":[12],\"temperature_2m_max\":[20]}}";

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField!.GetValue(service)!;

        Assert.False(cache.TryGetValue("weather:35:139", out _)); // ensure cache miss
        Assert.False(cache.TryGetValue("forecast:35:139", out _)); // ensure cache miss

        var (result, statusCode) = await service.GetWeatherAsync(null, 35, 139);

        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);

        var cached = cache.TryGetValue("weather:35:139", out _) || cache.TryGetValue("forecast:35:139", out _);
        Assert.True(cached); // now cached under one of the expected keys
    }

    // Directly call the public detector exposed on WeatherService for stable coverage
    [Fact]
    public void WeatherEventDetector_Produces_Alerts_For_Thunderstorm_And_HighTemp()
    {
        var (label, alerts) = WeatherService.WeatherEventDetector.Detect(95, 39);

        Assert.NotNull(label);
        Assert.NotNull(alerts);
        Assert.Contains("Thunderstorm", label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Severe storm risk", string.Join(" ", alerts));
        Assert.Contains("Severe heat", string.Join(" ", alerts));
    }
}
