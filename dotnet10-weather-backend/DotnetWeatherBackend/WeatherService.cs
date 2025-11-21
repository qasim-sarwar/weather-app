using DotnetWeatherBackend;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

public class WeatherService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly WeatherApiOptions _options;
    private readonly ILogger<WeatherService> _logger;

    private const string CoordsCachePrefix = "coords:";
    private const string ForecastCachePrefix = "forecast:";

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
            // Normalize city and resolve to coordinates if needed
            if (!string.IsNullOrWhiteSpace(city))
            {
                var normalized = city.Trim().ToLowerInvariant();
                (double lat, double lon) coords;
                var coordKey = CoordsCachePrefix + normalized;
                if (!_cache.TryGetValue<(double lat, double lon)>(coordKey, out coords))
                {
                    var geoUrl = $"{_options.GeoUrl}?name={Uri.EscapeDataString(city)}&count=1";
                    var geoResponse = await _httpClient.GetFromJsonAsync<GeoResponse>(geoUrl);
                    if (geoResponse?.Results == null || geoResponse.Results.Count == 0)
                    {
                        _logger.LogWarning("City not found: {City}", city);
                        return (new { error = "City not found" }, 404);
                    }
                    var first = geoResponse.Results[0];
                    coords = (first.Latitude, first.Longitude);
                    _cache.Set(coordKey, coords, TimeSpan.FromMinutes(30));
                }
                lat = coords.lat;
                lon = coords.lon;
                // keep original city param value for cache key generation below
                city = normalized;
            }

            if (lat == null || lon == null)
                return (new { error = "Either city or lat/lon must be provided" }, 400);

            string cacheKey = city != null ? $"{ForecastCachePrefix}{city}" : $"{ForecastCachePrefix}{lat}:{lon}";

            if (_cache.TryGetValue<WeatherForecast>(cacheKey, out var cachedForecast) && cachedForecast != null)
            {
                return (cachedForecast, 200);
            }

            // Ensure hourly includes weathercode (bug fix)
            var forecastUrl =
                $"{_options.BaseUrl.TrimEnd('/')}/{_options.ForecastEndpoint.TrimStart('/')}" +
                $"?latitude={lat}&longitude={lon}&hourly=temperature_2m,weathercode&daily=temperature_2m_min,temperature_2m_max,weathercode&current_weather=true&timezone=auto";

            var forecast = await _httpClient.GetFromJsonAsync<WeatherForecast>(forecastUrl);
            if (forecast == null)
                return (new { error = "Forecast API returned null" }, 500);

            var offsetSeconds = forecast.UtcOffsetSeconds ?? 0;
            var offset = TimeSpan.FromSeconds(offsetSeconds);

            // Build todayEntries
            var todayEntries = new System.Collections.Generic.List<HourlyEntry>();
            try
            {
                var hourlyTemps = forecast.Hourly?.Temperature2m;
                var hourlyTimes = forecast.Hourly?.Time;
                var hourlyCodes = forecast.Hourly?.WeatherCode;

                var todayDate = forecast.Daily?.Time?.FirstOrDefault();

                if (hourlyTemps != null && hourlyTimes != null)
                {
                    var len = Math.Min(hourlyTemps.Count, hourlyTimes.Count);
                    for (int i = 0; i < len; i++)
                    {
                        var t = hourlyTimes[i];
                        if (string.IsNullOrEmpty(t)) continue;

                        if (!string.IsNullOrEmpty(todayDate) && t.StartsWith(todayDate, StringComparison.Ordinal))
                        {
                            var parsed = DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.None);
                            var dto = new DateTimeOffset(parsed, offset);

                            var entry = new HourlyEntry
                            {
                                TimeIso = dto.ToString("o"),
                                DisplayTime = dto.ToString("h:mm tt", CultureInfo.InvariantCulture),
                                Temperature = hourlyTemps[i],
                                WeatherCode = (hourlyCodes != null && hourlyCodes.Count > i) ? (int?)hourlyCodes[i] : null
                            };

                            var det = WeatherEventDetector.Detect(entry.WeatherCode, entry.Temperature);
                            entry.Event = det.Label;
                            todayEntries.Add(entry);
                        }
                    }
                }

                if (todayEntries.Count == 0 && hourlyTemps != null && hourlyTimes != null)
                {
                    var len = Math.Min(24, Math.Min(hourlyTemps.Count, hourlyTimes.Count));
                    for (int i = 0; i < len; i++)
                    {
                        var parsed = DateTime.Parse(hourlyTimes[i], CultureInfo.InvariantCulture, DateTimeStyles.None);
                        var dto = new DateTimeOffset(parsed, offset);

                        var entry = new HourlyEntry
                        {
                            TimeIso = dto.ToString("o"),
                            DisplayTime = dto.ToString("h:mm tt", CultureInfo.InvariantCulture),
                            Temperature = hourlyTemps[i],
                            WeatherCode = (forecast.Hourly?.WeatherCode != null && forecast.Hourly.WeatherCode.Count > i) ? (int?)forecast.Hourly.WeatherCode[i] : null
                        };
                        var det = WeatherEventDetector.Detect(entry.WeatherCode, entry.Temperature);
                        entry.Event = det.Label;
                        todayEntries.Add(entry);
                    }
                    _logger.LogWarning("No hourly entries matched today's date; using first {Len} hours as fallback.", todayEntries.Count);
                }

                todayEntries = todayEntries.OrderBy(e =>
                {
                    if (DateTimeOffset.TryParse(e.TimeIso, out var parsed)) return parsed;
                    return DateTimeOffset.MinValue;
                }).ToList();

                forecast.TodayEntries = todayEntries;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build todayEntries.");
                forecast.TodayEntries = new System.Collections.Generic.List<HourlyEntry>();
            }

            // min/max
            if (forecast.TodayEntries != null && forecast.TodayEntries.Count > 0)
            {
                var minEntry = forecast.TodayEntries.Aggregate((a, b) => b.Temperature < a.Temperature ? b : a);
                var maxEntry = forecast.TodayEntries.Aggregate((a, b) => b.Temperature > a.Temperature ? b : a);

                forecast.MinTemp = minEntry.Temperature;
                forecast.MaxTemp = maxEntry.Temperature;
                forecast.MinTempTime = minEntry.TimeIso;
                forecast.MaxTempTime = maxEntry.TimeIso;
            }
            else
            {
                forecast.MinTemp = forecast.Daily?.Temperature2mMin?.FirstOrDefault();
                forecast.MaxTemp = forecast.Daily?.Temperature2mMax?.FirstOrDefault();
            }

            // normalize current weather time and attach day name / city
            if (forecast.CurrentWeather != null && !string.IsNullOrEmpty(forecast.CurrentWeather.Time))
            {
                var curDt = DateTime.Parse(forecast.CurrentWeather.Time, CultureInfo.InvariantCulture, DateTimeStyles.None);
                var curDto = new DateTimeOffset(curDt, offset);
                forecast.CurrentWeather.Time = curDto.ToString("o");
            }

            forecast.DayName = DateTime.UtcNow.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
            forecast.City = await GetCityNameFromApi(lat, lon);

            // Event detection summary
            var summary = WeatherEventDetector.Detect(forecast.CurrentWeather?.WeatherCode, forecast.CurrentWeather?.Temperature);
            forecast.EventForecast = summary.Label;

            // cache and return
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

    public static class WeatherEventDetector
    {
        // Returns a friendly event label and a list of alerts (deduplicated)
        public static (string Label, List<string> Alerts) Detect(int? weatherCode, double? tempOrMax)
        {
            var map = new Dictionary<int, string>
            {
                [0] = "Clear sky ☀️",
                [1] = "Mainly clear 🌤️",
                [2] = "Partly cloudy ⛅",
                [3] = "Overcast ☁️",
                [45] = "Fog 🌫️",
                [48] = "Rime fog 🌫️",
                [51] = "Light drizzle 🌦️",
                [53] = "Moderate drizzle 🌧️",
                [55] = "Dense drizzle 🌧️",
                [61] = "Slight rain 🌧️",
                [63] = "Moderate rain 🌧️",
                [65] = "Heavy rain 🌧️",
                [71] = "Slight snow 🌨️",
                [73] = "Moderate snow 🌨️",
                [75] = "Heavy snow ❄️",
                [77] = "Snow grains ❄️",
                [80] = "Rain showers 🌦️",
                [81] = "Heavy rain showers 🌧️",
                [82] = "Violent rain showers ⛈️",
                [85] = "Snow showers 🌨️",
                [86] = "Heavy snow showers ❄️",
                [95] = "Thunderstorm ⛈️",
                [96] = "Thunderstorm with hail ⛅",
                [99] = "Severe thunderstorm ⛈️"
            };

            string label = weatherCode.HasValue && map.TryGetValue(weatherCode.Value, out var lbl) ? lbl
                           : weatherCode.HasValue ? $"Code {weatherCode.Value}" : "Unknown";

            var alerts = new List<string>();

            if (weatherCode.HasValue && new[] { 95, 96, 99 }.Contains(weatherCode.Value))
                alerts.Add("Severe storm risk ⚡");
            if (weatherCode.HasValue && new[] { 71, 73, 75, 77 }.Contains(weatherCode.Value))
                alerts.Add("Blizzard risk ❄️");
            if (weatherCode.HasValue && new[] { 81, 82, 63, 65 }.Contains(weatherCode.Value))
                alerts.Add("Heavy precipitation / flood risk 🌧️");
            if (weatherCode.HasValue && new[] { 45, 48 }.Contains(weatherCode.Value))
                alerts.Add("Dense Fog 🌫️");

            if (tempOrMax.HasValue)
            {
                var t = tempOrMax.Value;
                if (t >= 42) alerts.Add("Extreme heat 🔥🔥");
                else if (t >= 38) alerts.Add("Severe heat 🔥");
                else if (t >= 35) alerts.Add("Heatwave 🔥");

                if (t <= -40) alerts.Add("Extreme polar cold ❄️❄️");
                else if (t <= -30) alerts.Add("Extreme cold ❄️");
                else if (t <= -20) alerts.Add("Severe cold ⚠️");
                else if (t <= -5) alerts.Add("Very cold 🥶");
                else if (t <= 0) alerts.Add("Freezing ❄️");
            }

            if (alerts.Count == 0)
                alerts.Add("No severe events detected ✅");

            // Deduplicate while preserving order
            var deduped = new List<string>();
            foreach (var a in alerts)
            {
                if (!deduped.Contains(a)) deduped.Add(a);
            }

            return (label, deduped);
        }
    }

    public async Task<string> GetCityNameFromApi(double? lat, double? lon)
    {
        if (lat == null || lon == null)
            return "Invalid coordinates.";

        string apiUrl = $"{_options.CityBaseUrl}?format=json&lat={lat}&lon={lon}";

        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DotnetWeatherBackend/10.0 (get@CityName.com)");

            var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = System.Text.Json.JsonSerializer.Deserialize<NominatimResponse>(json, options);

            string? cityName = data?.Address?.City ??
                               data?.Address?.Town ??
                               data?.Address?.Village ??
                               data?.Address?.State;

            return !string.IsNullOrEmpty(cityName) ? cityName : $"{lat},{lon}";
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "Error calling Nominatim API for city name lookup");
            return "City lookup failed (network error).";
        }
        catch (System.Text.Json.JsonException e)
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