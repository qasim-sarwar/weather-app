using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

public class WeatherService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly WeatherApiOptions _options;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<WeatherApiOptions> options,
        ILogger<WeatherService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(object result, int statusCode)> GetWeatherAsync(string? city, double? lat, double? lon)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(city))
            {
                city = city.Trim().ToLower();

                if (!_cache.TryGetValue(city, out (double lat, double lon) coords))
                {
                    var geoUrl = $"{_options.GeoUrl}?name={city}&count=1";
                    var geoResponse = await _httpClient.GetFromJsonAsync<GeoResponse>(geoUrl);

                    if (geoResponse?.results == null || geoResponse.results.Count == 0)
                        return (new { error = "City not found" }, 404);

                    coords = (geoResponse.results[0].latitude, geoResponse.results[0].longitude);
                    _cache.Set(city, coords, TimeSpan.FromMinutes(30));
                }

                lat = coords.lat;
                lon = coords.lon;
            }

            if (lat == null || lon == null)
                return (new { error = "Either city or lat/lon must be provided" }, 400);

            string cacheKey = city != null ? $"weather:{city}" : $"weather:{lat}:{lon}";
            if (_cache.TryGetValue(cacheKey, out WeatherForecast cachedForecast))
                return (cachedForecast, 200);

            var forecastUrl =
                $"{_options.BaseUrl}/{_options.ForecastEndpoint}?latitude={lat}&longitude={lon}&hourly=temperature_2m&daily=temperature_2m_min,temperature_2m_max&current_weather=true&timezone=auto";

            var forecast = await _httpClient.GetFromJsonAsync<WeatherForecast>(forecastUrl);
            if (forecast == null)
                return (new { error = "Forecast API returned null" }, 500);

            // Determine min/max temperatures and their times
            if (forecast.hourly?.temperature_2m != null && forecast.hourly.time != null)
            {
                var temps = forecast.hourly.temperature_2m;
                var times = forecast.hourly.time;

                var minTemp = temps.Min();
                var maxTemp = temps.Max();
                var minTime = times[temps.IndexOf(minTemp)];
                var maxTime = times[temps.IndexOf(maxTemp)];

                forecast.MinTemp = minTemp;
                forecast.MaxTemp = maxTemp;
                forecast.MinTempTime = minTime;
                forecast.MaxTempTime = maxTime;
            }

            forecast.EventForecast = DetectSevereWeather(forecast.current_weather?.weathercode ?? 0);

            _cache.Set(cacheKey, forecast, TimeSpan.FromMinutes(10));
            return (forecast, 200);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching weather");
            return (new { error = $"Network error: {ex.Message}" }, 503);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WeatherService");
            return (new { error = $"Unexpected error: {ex.Message}" }, 500);
        }
    }

    private string DetectSevereWeather(int weatherCode)
    {
        return weatherCode switch
        {
            95 or 96 or 99 => "Thunderstorm expected ⛈️",
            71 or 73 or 75 or 77 => "Blizzard conditions ❄️",
            45 or 48 => "Dense fog 🌫️",
            51 or 53 or 55 => "Light drizzle 🌦️",
            61 or 63 or 65 => "Rain showers 🌧️",
            80 or 81 or 82 => "Heavy rain showers ⛈",
            85 or 86 => "Snow showers 🌨️",
            _ => "Clear or mild weather ☀️"
        };
    }
}
