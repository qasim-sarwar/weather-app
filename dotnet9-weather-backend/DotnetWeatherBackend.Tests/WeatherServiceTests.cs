using DotnetWeatherBackend;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using Xunit;

public class WeatherServiceTests
{
    [Fact]
    public async Task GetWeatherAsync_ReturnsError_WhenNoCityOrLatLonProvided()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        // Create an HttpClient we won't use (no handlers necessary)
        var client = new HttpClient(new Mock<HttpMessageHandler>().Object);
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync(null, null, null);

        Assert.NotNull(result);
        Assert.Equal(400, statusCode);
        Assert.Contains("Either city or lat/lon must be provided", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsError_WhenCityNotFound()
    {
        // Mock handler to return {"results": []} for geocoding
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

        var client = new HttpClient(mockHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync("InvalidCity", null, null);

        Assert.NotNull(result);
        Assert.Equal(404, statusCode);
        Assert.Contains("City not found", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeather_WhenLatLonProvided()
    {
        // Mock handler to return forecast JSON when forecast URL is called
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

        var client = new HttpClient(mockHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync(null, 10, 20);

        Assert.NotNull(result);
        Assert.Equal(200, statusCode);
        Assert.Contains("current_weather", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeather_WhenCityFound()
    {
        // Geocoding returns coordinates, forecast returns current_weather
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

        var client = new HttpClient(mockHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.NotNull(result);
        Assert.Equal(200, statusCode);
        Assert.Contains("current_weather", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsServerError_WhenForecastServiceFails()
    {
        // Geocoding returns coordinates, forecast returns 500
        var geoJson = "{\"results\":[{\"latitude\":35.0,\"longitude\":139.0}]}";

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

        // Forecast call fails
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("api.open-meteo.com/v1/forecast")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Server error")
            });

        var client = new HttpClient(mockHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync("Tokyo", null, null);

        Assert.NotNull(result);
        Assert.Equal(500, statusCode);
        Assert.Contains("Failed to fetch weather", result.ToString());
    }

    [Fact]
    public async Task GetWeatherAsync_TrimsCityName_AndWorksWithWhitespace()
    {
        // Geocoding returns coordinates, forecast returns current_weather
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

        var client = new HttpClient(mockHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new WeatherService(mockFactory.Object);

        var (result, statusCode) = await service.GetWeatherAsync("   Tokyo   ", null, null);

        Assert.NotNull(result);
        Assert.Equal(200, statusCode);
        Assert.Contains("current_weather", result.ToString());
    }
}
