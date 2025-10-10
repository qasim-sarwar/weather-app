using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Globalization;

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
            // Resolve city -> lat/lon (with cache)
            if (!string.IsNullOrWhiteSpace(city))
            {
                city = city.Trim().ToLowerInvariant();

                if (!_cache.TryGetValue<(double lat, double lon)>(city, out var coords))
                {
                    var geoUrl = $"{_options.GeoUrl}?name={city}&count=1";
                    var geoResponse = await _httpClient.GetFromJsonAsync<GeoResponse>(geoUrl);

                    if (geoResponse?.results == null || geoResponse.results.Count == 0)
                    {
                        _logger.LogWarning("City not found: {City}", city);
                        return (new { error = "City not found" }, 404);
                    }

                    coords = (geoResponse.results[0].latitude, geoResponse.results[0].longitude);
                    _cache.Set(city, coords, TimeSpan.FromMinutes(30));
                }

                lat = coords.lat;
                lon = coords.lon;
            }

            // Validate input
            if (lat == null || lon == null)
            {
                return (new { error = "Either city or lat/lon must be provided" }, 400);
            }

            // Cache key for weather payload
            string cacheKey = city != null ? $"weather:{city}" : $"weather:{lat}:{lon}";

            if (_cache.TryGetValue<WeatherForecast>(cacheKey, out var cachedForecast) && cachedForecast != null)
            {
                return (cachedForecast, 200);
            }

            // Request hourly + daily + current (timezone=auto to get utc_offset_seconds)
            var forecastUrl =
                $"{_options.BaseUrl}/{_options.ForecastEndpoint}?latitude={lat}&longitude={lon}&hourly=temperature_2m&daily=temperature_2m_min,temperature_2m_max,weathercode&current_weather=true&timezone=auto";

            var forecast = await _httpClient.GetFromJsonAsync<WeatherForecast>(forecastUrl);
            if (forecast == null)
                return (new { error = "Forecast API returned null" }, 500);

            // --- compute Min/Max and times for *today* using hourly data ---
            try
            {
                var offsetSeconds = forecast.utc_offset_seconds ?? 0;
                var offset = TimeSpan.FromSeconds(offsetSeconds);

                // today's date string from daily.time[0] (Open-Meteo convention)
                string? todayDate = forecast.daily?.time?.FirstOrDefault(); // e.g. "2025-10-09"

                var hourlyTemps = forecast.hourly?.temperature_2m;
                var hourlyTimes = forecast.hourly?.time;

                var todayEntries = new List<(double temp, string time)>();

                if (!string.IsNullOrEmpty(todayDate) && hourlyTemps != null && hourlyTimes != null)
                {
                    int len = Math.Min(hourlyTemps.Count, hourlyTimes.Count);
                    for (int i = 0; i < len; i++)
                    {
                        var t = hourlyTimes[i];
                        if (!string.IsNullOrEmpty(t) && t.StartsWith(todayDate, StringComparison.Ordinal))
                        {
                            todayEntries.Add((hourlyTemps[i], t));
                        }
                    }
                }

                // Fallback: if no hour entries for today, take first 24 hours as best-effort
                if (todayEntries.Count == 0 && hourlyTemps != null && hourlyTimes != null && hourlyTemps.Count > 0)
                {
                    int len = Math.Min(24, Math.Min(hourlyTemps.Count, hourlyTimes.Count));
                    for (int i = 0; i < len; i++)
                        todayEntries.Add((hourlyTemps[i], hourlyTimes[i]));

                    _logger.LogWarning("No hourly entries matched today's date; using first {Len} hours as fallback.", todayEntries.Count);
                }

                if (todayEntries.Count > 0)
                {
                    // compute min/max from today's entries
                    var minEntry = todayEntries.Aggregate((a, b) => b.temp < a.temp ? b : a);
                    var maxEntry = todayEntries.Aggregate((a, b) => b.temp > a.temp ? b : a);

                    forecast.MinTemp = minEntry.temp;
                    forecast.MaxTemp = maxEntry.temp;

                    // Parse naive ISO strings and attach offset from API
                    var minDt = DateTime.Parse(minEntry.time, CultureInfo.InvariantCulture, DateTimeStyles.None);
                    var maxDt = DateTime.Parse(maxEntry.time, CultureInfo.InvariantCulture, DateTimeStyles.None);

                    var minDto = new DateTimeOffset(minDt, offset);
                    var maxDto = new DateTimeOffset(maxDt, offset);

                    forecast.MinTempTime = minDto.ToString("o"); // ISO8601 with offset
                    forecast.MaxTempTime = maxDto.ToString("o");
                }
                else
                {
                    // fallback to daily summary if hourly unavailable
                    forecast.MinTemp = forecast.daily?.temperature_2m_min?.FirstOrDefault();
                    forecast.MaxTemp = forecast.daily?.temperature_2m_max?.FirstOrDefault();
                    forecast.MinTempTime = null;
                    forecast.MaxTempTime = null;
                }

                // Normalize current_weather.time to include offset (if present)
                if (forecast.current_weather != null && !string.IsNullOrEmpty(forecast.current_weather.time))
                {
                    var curDt = DateTime.Parse(forecast.current_weather.time, CultureInfo.InvariantCulture, DateTimeStyles.None);
                    var curDto = new DateTimeOffset(curDt, offset);
                    forecast.current_weather.time = curDto.ToString("o");
                }
            }
            catch (Exception ex)
            {
                // don't fail the whole request if time parsing fails — log and continue
                _logger.LogWarning(ex, "Failed to compute min/max times or normalize timezones. Returning raw times.");
            }

            // Event detection (existing logic)
            forecast.EventForecast = DetectSevereWeather(forecast.current_weather?.weathercode ?? 0);

            // Cache and return
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
