using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetWeatherBackend
{
    // Configuration options DTO
    public class WeatherApiOptions
    {
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1";

        [JsonPropertyName("forecastEndpoint")]
        public string ForecastEndpoint { get; set; } = "forecast";

        [JsonPropertyName("geoUrl")]
        public string GeoUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";

        [JsonPropertyName("cityBaseUrl")]
        public string CityBaseUrl { get; set; } = "https://nominatim.openstreetmap.org/reverse";
    }

    // Primary forecast DTO returned to frontend (root object from Open-Meteo + enriched fields)
    public class WeatherForecast
    {
        // Open-Meteo root fields
        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }

        [JsonPropertyName("generationtime_ms")]
        public double? GenerationTimeMs { get; set; }

        [JsonPropertyName("utc_offset_seconds")]
        public int? UtcOffsetSeconds { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("timezone_abbreviation")]
        public string? TimezoneAbbreviation { get; set; }

        [JsonPropertyName("elevation")]
        public double? Elevation { get; set; }

        // Open-Meteo standard blocks
        [JsonPropertyName("current_weather")]
        public CurrentWeather? CurrentWeather { get; set; }

        [JsonPropertyName("hourly")]
        public Hourly? Hourly { get; set; }

        [JsonPropertyName("daily")]
        public Daily? Daily { get; set; }

        // Optional units blocks (if present)
        [JsonPropertyName("hourly_units")]
        public Dictionary<string, string>? HourlyUnits { get; set; }

        [JsonPropertyName("daily_units")]
        public Dictionary<string, string>? DailyUnits { get; set; }

        // Computed / enriched fields (returned to frontend)
        [JsonPropertyName("minTemp")]
        public double? MinTemp { get; set; }

        [JsonPropertyName("maxTemp")]
        public double? MaxTemp { get; set; }

        [JsonPropertyName("minTempTime")]
        public string? MinTempTime { get; set; }   // ISO timestamp with offset

        [JsonPropertyName("maxTempTime")]
        public string? MaxTempTime { get; set; }   // ISO timestamp with offset

        [JsonPropertyName("dayName")]
        public string? DayName { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("eventForecast")]
        public string? EventForecast { get; set; }

        [JsonPropertyName("todayEntries")]
        public List<HourlyEntry>? TodayEntries { get; set; }
    }

    // Current weather block
    public class CurrentWeather
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }  // ISO string (we normalize to include offset)

        [JsonPropertyName("windspeed")]
        public double? WindSpeed { get; set; }

        [JsonPropertyName("winddirection")]
        public double? WindDirection { get; set; }

        [JsonPropertyName("weathercode")]
        public int? WeatherCode { get; set; }
    }

    // Hourly block from Open-Meteo
    public class Hourly
    {
        [JsonPropertyName("time")]
        public List<string>? Time { get; set; }

        [JsonPropertyName("temperature_2m")]
        public List<double>? Temperature2m { get; set; }

        [JsonPropertyName("weathercode")]
        public List<int>? WeatherCode { get; set; }

        // Add other hourly variables if you use them (e.g., relativehumidity_2m)
        [JsonPropertyName("relativehumidity_2m")]
        public List<double>? RelativeHumidity2m { get; set; }
    }

    // Daily block from Open-Meteo
    public class Daily
    {
        [JsonPropertyName("time")]
        public List<string>? Time { get; set; }

        [JsonPropertyName("temperature_2m_min")]
        public List<double>? Temperature2mMin { get; set; }

        [JsonPropertyName("temperature_2m_max")]
        public List<double>? Temperature2mMax { get; set; }

        [JsonPropertyName("weathercode")]
        public List<int>? WeatherCode { get; set; }
    }

    // Hourly entry returned in todayEntries
    public class HourlyEntry
    {
        // ISO timestamp with offset (e.g. 2025-10-09T10:00:00+09:00)
        [JsonPropertyName("timeIso")]
        public string TimeIso { get; set; } = string.Empty;

        // Human readable (e.g. "10:00 AM")
        [JsonPropertyName("displayTime")]
        public string DisplayTime { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        // nullable integer from hourly.weathercode
        [JsonPropertyName("weatherCode")]
        public int? WeatherCode { get; set; }

        // friendly short label (e.g. "Clear sky ☀️")
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;
    }

    // GeoResponse for open-meteo geocoding API
    public class GeoResponse
    {
        //[JsonPropertyName("results")]
        public List<GeoResult>? Results { get; set; }
    }

    public class GeoResult
    {
        // keep raw JSON here because 'id' may be number or string
        [JsonPropertyName("id")]
        public JsonElement Id { get; set; }

        [JsonIgnore]
        public string? IdString
        {
            get
            {
                if (Id.ValueKind == JsonValueKind.String) return Id.GetString();
                if (Id.ValueKind == JsonValueKind.Number) return Id.GetRawText(); // numeric text
                return null;
            }
        }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; set; } // region/state

        [JsonPropertyName("admin2")]
        public string? Admin2 { get; set; } // county/district

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("population")]
        public long? Population { get; set; }
    }

    // Nominatim response DTO (reverse geocoding)
    public class NominatimResponse
    {
        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; set; }
    }

    public class NominatimAddress
    {
        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("town")]
        public string? Town { get; set; }

        [JsonPropertyName("village")]
        public string? Village { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }
    }
}
