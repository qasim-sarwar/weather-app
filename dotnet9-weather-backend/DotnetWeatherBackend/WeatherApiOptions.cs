namespace DotnetWeatherBackend
{
    public class WeatherApiOptions
    {
        public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1";
        public string GeoEndpoint { get; set; } = "geocoding";
        public string ForecastEndpoint { get; set; } = "forecast";
        public object GeoUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";
    }
}
