namespace DotnetWeatherBackend
{
    public class WeatherApiOptions
    {
        public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1";
        public string GeoEndpoint { get; set; } = "geocoding";
        public string ForecastEndpoint { get; set; } = "forecast";
        public object GeoUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";
    }
    public class WeatherForecast
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
        public CurrentWeather? current_weather { get; set; }
        public Daily? daily { get; set; }
        public Hourly? hourly { get; set; }

        // Open-Meteo meta fields
        public int? utc_offset_seconds { get; set; }            // e.g. 32400 for JST
        public string? timezone { get; set; }                   // e.g. "Asia/Tokyo"
        public string? timezone_abbreviation { get; set; }

        // Custom calculated fields
        public double? MinTemp { get; set; }
        public double? MaxTemp { get; set; }
        public string? MinTempTime { get; set; }    // ISO8601 with offset
        public string? MaxTempTime { get; set; }    // ISO8601 with offset
        public string? EventForecast { get; set; }
    }

    public class CurrentWeather
    {
        public double temperature { get; set; }
        public double windspeed { get; set; }
        public double winddirection { get; set; }
        public string time { get; set; } = string.Empty;
        public int weathercode { get; set; }
    }

    public class Daily
    {
        public List<double>? temperature_2m_min { get; set; }
        public List<double>? temperature_2m_max { get; set; }
        public List<string>? time { get; set; }
    }

    public class Hourly
    {
        public List<double>? temperature_2m { get; set; }
        public List<string>? time { get; set; }
    }

    public class GeoResponse
    {
        public List<GeoResult>? results { get; set; }
    }

    public class GeoResult
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
    }
}