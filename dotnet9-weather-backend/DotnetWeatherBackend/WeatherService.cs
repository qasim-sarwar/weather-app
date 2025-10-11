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
            // city -> coords (existing logic)...
            if (!string.IsNullOrWhiteSpace(city))
            {
                city = city.Trim().ToLowerInvariant();
                if (!_cache.TryGetValue<(double lat, double lon)>(city, out var coords))
                {
                    var geoUrl = $"{_options.GeoUrl}?name={city}&count=1";
                    var geoResponse = await _httpClient.GetFromJsonAsync<GeoResponse>(geoUrl);
                    if (geoResponse?.Results == null || geoResponse.Results.Count == 0)
                    {
                        _logger.LogWarning("City not found: {City}", city);
                        return (new { error = "City not found" }, 404);
                    }
                    coords = (geoResponse.Results[0].Latitude, geoResponse.Results[0].Longitude);
                    _cache.Set(city, coords, TimeSpan.FromMinutes(30));
                }
                lat = coords.lat;
                lon = coords.lon;
            }

            if (lat == null || lon == null)
                return (new { error = "Either city or lat/lon must be provided" }, 400);

            // cache key (keep consistent)
            string cacheKey = city != null ? $"weather:{city}" : $"weather:{lat}:{lon}";

            // Return from cache immediately if present
            if (_cache.TryGetValue<WeatherForecast>(cacheKey, out var cachedForecast) && cachedForecast != null)
            {
                return (cachedForecast, 200);
            }

            // Request hourly including weathercode now (so we can build per-hour events)
            var forecastUrl =
                $"{_options.BaseUrl}/{_options.ForecastEndpoint}?latitude={lat}&longitude={lon}&hourly=temperature_2m&daily=temperature_2m_min,temperature_2m_max,weathercode&current_weather=true&timezone=auto";

            var forecast = await _httpClient.GetFromJsonAsync<WeatherForecast>(forecastUrl);
            if (forecast == null)
                return (new { error = "Forecast API returned null" }, 500);

            // Compute offset
            var offsetSeconds = forecast.UtcOffsetSeconds ?? 0;
            var offset = TimeSpan.FromSeconds(offsetSeconds);

            // Build todayEntries from hourly arrays
            var todayEntries = new List<HourlyEntry>();
            try
            {
                var hourlyTemps = forecast.Hourly?.Temperature2m;
                var hourlyTimes = forecast.Hourly?.Time;
                var hourlyCodes = forecast.Hourly?.WeatherCode; // might be null if not returned

                // Get today's date string from daily.time[0] if available
                string? todayDate = forecast.Daily?.Time?.FirstOrDefault(); // e.g. "2025-10-09"

                if (hourlyTemps != null && hourlyTimes != null)
                {
                    int len = Math.Min(hourlyTemps.Count, hourlyTimes.Count);
                    for (int i = 0; i < len; i++)
                    {
                        var t = hourlyTimes[i];
                        if (!string.IsNullOrEmpty(t))
                        {
                            // select only entries for today's date (or fallback later)
                            if (!string.IsNullOrEmpty(todayDate) && t.StartsWith(todayDate, StringComparison.Ordinal))
                            {
                                // parse time and attach offset so we can sort and format
                                var parsed = DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.None);
                                var dto = new DateTimeOffset(parsed, offset);

                                var entry = new HourlyEntry
                                {
                                    TimeIso = dto.ToString("o"),
                                    DisplayTime = dto.ToString("h:mm tt", CultureInfo.InvariantCulture),
                                    Temperature = hourlyTemps[i],
                                    WeatherCode = (hourlyCodes != null && hourlyCodes.Count > i) ? (int?)hourlyCodes[i] : null
                                };
                                entry.Event = DetectSevereWeather(entry.WeatherCode ?? 0, entry.Temperature);
                                todayEntries.Add(entry);
                            }
                        }
                    }
                }

                // fallback: if nothing matched today's date, take first 24 hours (best-effort)
                if (todayEntries.Count == 0 && hourlyTemps != null && hourlyTimes != null)
                {
                    int len = Math.Min(24, Math.Min(hourlyTemps.Count, hourlyTimes.Count));
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
                        entry.Event = DetectSevereWeather(entry.WeatherCode ?? 0, entry.Temperature);
                        todayEntries.Add(entry);
                    }
                    _logger.LogWarning("No hourly entries matched today's date; using first {Len} hours as fallback.", todayEntries.Count);
                }

                // sort by local time ascending (AM -> PM)
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
                forecast.TodayEntries = new List<HourlyEntry>();
            }

            // compute min/max/time as before (use todayEntries for min/max if present)
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

            // normalize current weather time
            if (forecast.CurrentWeather != null && !string.IsNullOrEmpty(forecast.CurrentWeather.Time))
            {
                var curDt = DateTime.Parse(forecast.CurrentWeather.Time, CultureInfo.InvariantCulture, DateTimeStyles.None);
                var curDto = new DateTimeOffset(curDt, offset);
                forecast.CurrentWeather.Time = curDto.ToString("o");
                forecast.DayName = DateTime.UtcNow.DayOfWeek.ToString();
                forecast.City = await GetCityNameFromApi(lat, lon);
            }

            // attach City (existing call)
            forecast.City = await GetCityNameFromApi(lat, lon);

            // Event detection
            forecast.EventForecast = DetectSevereWeather(forecast.CurrentWeather?.WeatherCode ?? 0, forecast.MaxTemp);

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

    private string DetectSevereWeather(int weatherCode, double? currentTemp)
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
            else if (currentTemp >= 42)
                parts.Add("Extreme heat 🔥🔥");
            else if (currentTemp >= 38)
                parts.Add("Severe heat 🔥");
            else if (currentTemp >= 35)
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