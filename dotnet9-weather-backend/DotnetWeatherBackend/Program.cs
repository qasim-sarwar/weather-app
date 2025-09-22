namespace DotnetWeatherBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddAuthorization();
            builder.Services.AddHttpClient();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAngular", policy =>
                    policy.WithOrigins("http://localhost:4200")
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            builder.Services.AddOpenApi();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAngular");
            app.UseAuthorization();

            // Match Angular service params (lat, lon, city)
            app.MapGet("/api/weather", async (string? city, double? lat, double? lon, IHttpClientFactory httpClientFactory) =>
            {
                var client = httpClientFactory.CreateClient();
                string url;

                if (!string.IsNullOrEmpty(city))
                {
                    // Example: use Open-Meteo geocoding to get lat/lon by city
                    var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1";
                    var geo = await client.GetFromJsonAsync<dynamic>(geoUrl);

                    if (geo?["results"] == null || geo["results"].Count == 0)
                    {
                        return Results.NotFound(new { error = "City not found" });
                    }

                    lat = (double)geo["results"][0]["latitude"];
                    lon = (double)geo["results"][0]["longitude"];
                }

                if (lat == null || lon == null)
                {
                    return Results.BadRequest(new { error = "Either city or lat/lon must be provided" });
                }

                url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";

                try
                {
                    var response = await client.GetFromJsonAsync<object>(url);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to fetch weather: {ex.Message}");
                }
            })
            .WithName("GetWeather");

            app.Run();
        }
    }
}
