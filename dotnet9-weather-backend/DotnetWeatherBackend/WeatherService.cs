using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

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
            string cacheKey = city != null ? $"{city}" : $"{lat},{lon}";

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

            // compute Min/Max and times for today using hourly data
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
                    forecast.DayName = DateTime.UtcNow.DayOfWeek.ToString();
                    forecast.City = await GetCityNameFromApi(lat, lon);
                }
            }
            catch (Exception ex)
            {
                // don't fail the whole request if time parsing fails — log and continue
                _logger.LogWarning(ex, "Failed to compute min/max times or normalize timezones. Returning raw times.");
            }

            // Event detection (existing logic)
            forecast.EventForecast = DetectSevereWeather(forecast.current_weather?.weathercode ?? 0, forecast.current_weather?.temperature, forecast.MaxTemp);

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

    private string DetectSevereWeather(int weatherCode, double? currentTemp, double? maxTemp)
    {
        var parts = new List<string>();

        // Map WMO weather codes to friendly labels
        var codeLabel = weatherCode switch
        {
            0 => "Clear sky ☀️",
            1 => "Mainly clear 🌤️",
            2 => "Partly cloudy ⛅",
            3 => "Overcast ☁️",
            45 => "Fog 🌫️",
            48 => "Rime fog 🌫️",
            51 => "Light drizzle 🌦️",
            53 => "Moderate drizzle 🌧️",
            55 => "Dense drizzle 🌧️",
            61 => "Slight rain 🌧️",
            63 => "Moderate rain 🌧️",
            65 => "Heavy rain 🌧️",
            71 => "Slight snow 🌨️",
            73 => "Moderate snow 🌨️",
            75 => "Heavy snow ❄️",
            77 => "Snow grains ❄️",
            80 => "Rain showers 🌦️",
            81 => "Heavy rain showers 🌧️",
            82 => "Violent rain showers ⛈️",
            85 => "Snow showers 🌨️",
            86 => "Heavy snow showers ❄️",
            95 => "Thunderstorm ⛈️",
            96 => "Thunderstorm with hail ⛈️",
            99 => "Severe thunderstorm ⛈️",
            _ => $"Code {weatherCode}"
        };

        parts.Add(codeLabel);

        // Temperature-based alerts (cold first, then heat)
        if (currentTemp.HasValue)
        {
            if (currentTemp <= -40)
                parts.Add("Extreme polar cold ❄️❄️");
            else if (currentTemp <= -30)
                parts.Add("Extreme cold ❄️");
            else if (currentTemp <= -20)
                parts.Add("Severe cold ⚠️");
            else if (currentTemp <= -5)
                parts.Add("Very cold 🥶");
            else if (currentTemp <= 0)
                parts.Add("Freezing ❄️");
        }

        if (maxTemp.HasValue)
        {
            if (maxTemp >= 42)
                parts.Add("Extreme heat 🔥🔥");
            else if (maxTemp >= 38)
                parts.Add("Severe heat 🔥");
            else if (maxTemp >= 35)
                parts.Add("Heatwave 🔥");
        }

        // Include a short "severity summary" if thunderstorm / blizzard etc.
        if (new[] { 95, 96, 99 }.Contains(weatherCode))
            parts.Add("Severe storm risk ⚡");
        if (new[] { 71, 73, 75, 77 }.Contains(weatherCode))
            parts.Add("Blizzard risk ❄️");
        if (new[] { 81, 82, 63, 65 }.Contains(weatherCode))
            parts.Add("Heavy precipitation / flood risk 🌧️");

        // Deduplicate and return
        var result = string.Join(" ", parts.Distinct());
        return result;
    }
    public async Task<string> GetCityNameFromApi(double? lat, double? lon)
    {
        if (lat == null || lon == null)
            return "Invalid coordinates.";

        string apiUrl = $"{_options.CityBaseUrl}?format=json&lat={lat}&lon={lon}";

        try
        {
            // Required by Nominatim to prevent 403 responses
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DotnetWeatherBackend/1.0 (contact@example.com)");

            var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var data = JsonSerializer.Deserialize<NominatimResponse>(json, options);

            // Try city, then town, then village
            string? cityName = data?.Address?.City ??
                               data?.Address?.Town ??
                               data?.Address?.Village ??
                               data?.Address?.State;

            return !string.IsNullOrEmpty(cityName) ? cityName : $"{lat},{lon}";
            ;
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "Error calling Nominatim API for city name lookup");
            return "City lookup failed (network error).";
        }
        catch (JsonException e)
        {
            _logger.LogError(e, "Error parsing Nominatim API response");
            return "City lookup failed (invalid response).";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error in GetCityNameFromApi");
            return "City lookup failed (unexpected error).";
        }
    }
}