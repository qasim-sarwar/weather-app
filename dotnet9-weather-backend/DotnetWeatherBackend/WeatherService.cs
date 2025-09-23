using System.Text.Json;

namespace DotnetWeatherBackend
{
    public class WeatherService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public WeatherService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<(object? Result, int StatusCode)> GetWeatherAsync(string? city, double? lat, double? lon)
        {
            var client = _httpClientFactory.CreateClient();

            if (!string.IsNullOrEmpty(city))
            {
                var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1";
                var geo = await client.GetFromJsonAsync<JsonElement>(geoUrl);

                if (!geo.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    return (new { error = "City not found" }, 404);
                }

                lat = results[0].GetProperty("latitude").GetDouble();
                lon = results[0].GetProperty("longitude").GetDouble();
            }

            if (lat == null || lon == null)
            {
                return (new { error = "Either city or lat/lon must be provided" }, 400);
            }

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";

            try
            {
                var response = await client.GetFromJsonAsync<object>(url);
                return (response, 200);
            }
            catch (Exception ex)
            {
                return (new { error = $"Failed to fetch weather: {ex.Message}" }, 500);
            }
        }
    }
}