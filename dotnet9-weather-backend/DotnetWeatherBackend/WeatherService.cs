using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DotnetWeatherBackend
{
    // Options to load from appsettings.json
    public class WeatherApiOptions
    {
        public string GeoUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";
        public string ForecastUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";
    }

    // DTOs for deserialization
    public class GeoResponse
    {
        public List<GeoResult>? results { get; set; }
    }

    public class GeoResult
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
    }

    public class ForecastResponse
    {
        public CurrentWeather? current_weather { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
    }

    public class CurrentWeather
    {
        public double temperature { get; set; }
        public double windspeed { get; set; }
        public double winddirection { get; set; }
        public string time { get; set; }
        public int weathercode { get; set; }
    }

    public class WeatherService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly WeatherApiOptions _options;
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IOptions<WeatherApiOptions> options,
            ILogger<WeatherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<(object result, int statusCode)> GetWeatherAsync(string? city, double? lat, double? lon)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                // If city is provided, resolve lat/lon (with caching)
                if (!string.IsNullOrWhiteSpace(city))
                {
                    city = city.Trim().ToLower();

                    if (!_cache.TryGetValue(city, out (double lat, double lon) coOrds))
                    {
                        var geoUrl = $"{_options.GeoUrl}?name={city}&count=1";
                        var geoResponse = await client.GetFromJsonAsync<GeoResponse>(geoUrl);

                        if (geoResponse?.results == null || geoResponse.results.Count == 0)
                        {
                            _logger.LogWarning("City not found: {City}", city);
                            return (new { error = "City not found" }, 404);
                        }

                        coOrds = (geoResponse.results[0].latitude, geoResponse.results[0].longitude);

                        // Cache result for 30 minutes
                        _cache.Set(city, coOrds, TimeSpan.FromMinutes(30));
                    }

                    lat = coOrds.lat;
                    lon = coOrds.lon;
                }

                // Validate input
                if (lat == null || lon == null)
                {
                    return (new { error = "Either city or lat/lon must be provided" }, 400);
                }

                // Create a cache key for weather data
                string weatherCacheKey = city != null
                    ? $"weather:{city}"
                    : $"weather:{lat}:{lon}";

                // Try to get weather data from cache
                if (_cache.TryGetValue(weatherCacheKey, out ForecastResponse cachedForecast))
                {
                    return (cachedForecast, 200);
                }

                // Call forecast API
                var url = $"{_options.ForecastUrl}?latitude={lat}&longitude={lon}&current_weather=true";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Forecast API returned {StatusCode} for lat={Lat}, lon={Lon}",
                        response.StatusCode, lat, lon);

                    return (new { error = $"Forecast API failed with status {response.StatusCode}" },
                        (int)response.StatusCode);
                }

                var forecast = await response.Content.ReadFromJsonAsync<ForecastResponse>();

                // Cache weather data for 10 minutes
                _cache.Set(weatherCacheKey, forecast, TimeSpan.FromMinutes(10));

                return (forecast!, 200);
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
    }
}
