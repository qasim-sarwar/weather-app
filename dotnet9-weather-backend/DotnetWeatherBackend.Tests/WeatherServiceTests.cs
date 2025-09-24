using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

public class WeatherServiceTests
{    private static WeatherService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);

        // Mock IOptions<WeatherApiOptions>
        var options = Options.Create(new WeatherApiOptions
        {
            GeoUrl = "https://geocoding-api.open-meteo.com/v1/search",
            ForecastUrl = "https://api.open-meteo.com/v1/forecast"
        });

        // Create IMemoryCache
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        // Create mock ILogger<WeatherService>
        var mockLogger = new Mock<ILogger<WeatherService>>();

        // Instantiate WeatherService with HttpClient (not IHttpClientFactory anymore)
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("geocoding-api.open-meteo.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoResponse, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("InvalidCity", null, null);

        Assert.NotNull(result);
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync(null, 10, 20);

        Assert.Equal(200, statusCode);
        var forecast = Assert.IsType<ForecastResponse>(result);
        Assert.NotNull(forecast.current_weather);
        Assert.Equal(20, forecast.current_weather.temperature);
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeather_WhenCityFound()
    {
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var forecastJson = "{\"current_weather\":{\"temperature\":22}}";

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        // Geocoding call
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("geocoding-api.open-meteo.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
            });

        // Forecast call
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.Equal(200, statusCode);
        var forecast = Assert.IsType<ForecastResponse>(result);
        Assert.Equal(22, forecast.current_weather.temperature);
    }
    [Fact]
    public async Task GetWeatherAsync_ReturnsServerError_WhenForecastServiceFails()
    {
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("geocoding-api.open-meteo.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
            });

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Server error")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.Equal(500, statusCode);
        Assert.Contains("Forecast API failed with status", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_TrimsCityName_AndWorksWithWhitespace()
    {
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";
        var forecastJson = "{\"current_weather\":{\"temperature\":22}}";

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("geocoding-api.open-meteo.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
            });

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("   Tokyo   ", null, null);

        Assert.Equal(200, statusCode);
        var forecast = Assert.IsType<ForecastResponse>(result);
        Assert.Equal(22, forecast.current_weather.temperature);
    }

    [Fact]
    public async Task GetWeatherAsync_CacheHit_DoesNotCallGeocodingApi()
    {
        var forecastJson = "{\"current_weather\":{\"temperature\":30}}";

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        // Only forecast call should be setup
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        // Manually add city to cache
        var cacheField = typeof(WeatherService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (IMemoryCache)cacheField.GetValue(service);
        cache.Set("london", (51.5074, -0.1278), TimeSpan.FromMinutes(30));

        var (result, statusCode) = await service.GetWeatherAsync("London", null, null);

        Assert.Equal(200, statusCode);
        var forecast = Assert.IsType<ForecastResponse>(result);
        Assert.Equal(30, forecast.current_weather.temperature);
    }
    
    [Fact]
    public async Task GetWeatherAsync_CacheMiss_CallsGeocodingApi()
    {
        var geoJson = "{\"results\":[{\"latitude\":40.0,\"longitude\":-74.0}]}";
        var forecastJson = "{\"current_weather\":{\"temperature\":25}}";

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        // Geocoding call
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("geocoding-api.open-meteo.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geoJson, Encoding.UTF8, "application/json")
            });

        // Forecast call
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(forecastJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(mockHandler.Object);

        var (result, statusCode) = await service.GetWeatherAsync("NewYork", null, null);

        Assert.Equal(200, statusCode);
        var forecast = Assert.IsType<ForecastResponse>(result);
        Assert.Equal(25, forecast.current_weather.temperature);
    }
}
