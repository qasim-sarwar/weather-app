using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

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
            GeoUrl = "geocoding",
            ForecastEndpoint = "forecast"
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

    [Fact]
    public async Task GetWeatherAsync_ReturnsError_WhenNoCityOrLatLonProvided()
    {
        var service = CreateService(new Mock<HttpMessageHandler>().Object);

        var (result, statusCode) = await service.GetWeatherAsync(null, null, null);

        Assert.NotNull(result);
        Assert.Equal(400, statusCode);
        Assert.Contains("Either city or lat/lon must be provided", result.ToString());
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
        Assert.Contains("City not found", result.ToString());
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
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(callCount == 1 ? geoJson : forecastJson, Encoding.UTF8, "application/json")
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
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);
        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.True(statusCode == 404 || statusCode == 500);
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

        // manually add coordinates to cache
        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField!.GetValue(service)!;
        cache.Set("london", (51.5074, -0.1278), TimeSpan.FromMinutes(30));

        var (result, statusCode) = await service.GetWeatherAsync("London", null, null);
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public async Task GetWeatherAsync_CacheHit_ReturnsCachedForecastWithoutApiCall()
    {
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict); // no API call expected
        var service = CreateService(mockHandler.Object);

        var forecast = new WeatherForecast
        {
            CurrentWeather = new CurrentWeather { Temperature = 25 },
            MinTemp = 20,
            MaxTemp = 30
        };

        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField!.GetValue(service)!;

        cache.Set("weather:35:139", forecast, TimeSpan.FromMinutes(10));

        var (result, statusCode) = await service.GetWeatherAsync(null, 35, 139);

        Assert.Equal(200, statusCode);
        Assert.Same(forecast, result); // same instance from cache
    }

    [Fact]
    public async Task GetWeatherAsync_CacheMiss_FetchesForecastAndStoresInCache()
    {
        var forecastJson = "{\"current_weather\":{\"temperature\":18},\"hourly\":{\"temperature_2m\":[15,18],\"time\":[\"2025-10-09T10:00\",\"2025-10-09T11:00\"]}}";

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

        var (result, statusCode) = await service.GetWeatherAsync(null, 35, 139);

        Assert.Equal(200, statusCode);
        Assert.IsType<WeatherForecast>(result);
        Assert.True(cache.TryGetValue("weather:35:139", out _)); // now cached
    }
}
