// server.js
import express from "express";
import axios from "axios";
import cors from "cors";

const app = express();
app.use(cors());
app.use(express.json());

// Error handling middleware
function errorHandler(err, req, res, next) {
  console.error("Error:", err.message);
  res.status(err.status || 500).json({
    error: true,
    message: err.message || "Internal Server Error",
  });
}

// Base URLs
const GEO_API = "https://geocoding-api.open-meteo.com/v1/search";
const WEATHER_API = "https://api.open-meteo.com/v1/forecast";
const REVERSE_GEO_API = "https://api.openweathermap.org/geo/1.0/reverse";

// Optional (for reverse geocoding)
const OPENWEATHER_API_KEY = process.env.OPENWEATHER_API_KEY || "YOUR_OPENWEATHER_API_KEY";

// Fetch weather by city
app.get("/api/weather", async (req, res, next) => {
  const { city, lat, lon } = req.query;

  try {
    let latitude = lat;
    let longitude = lon;

    // If city provided, resolve it to lat/lon
    if (city) {
      const geoRes = await axios.get(`${GEO_API}?name=${encodeURIComponent(city)}&count=1`);
      if (!geoRes.data.results || geoRes.data.results.length === 0) {
        return res.status(404).json({ error: true, message: "City not found" });
      }

      latitude = geoRes.data.results[0].latitude;
      longitude = geoRes.data.results[0].longitude;
    }

    if (!latitude || !longitude) {
      return res.status(400).json({ error: true, message: "Latitude and Longitude are required" });
    }

    const weatherUrl = `${WEATHER_API}?latitude=${latitude}&longitude=${longitude}&hourly=temperature_2m&daily=temperature_2m_min,temperature_2m_max,weathercode&current_weather=true&timezone=auto`;
    const weatherResponse = await axios.get(weatherUrl);

    const data = weatherResponse.data;
    data.city = city || null;
    data.latitude = latitude;
    data.longitude = longitude;

    res.json(data);
  } catch (err) {
    console.error("Weather API error:", err.message);
    next({ status: 500, message: "Failed to fetch weather data" });
  }
});

// Reverse geocoding: get city name from lat/lon
app.get("/api/reverse-geocode", async (req, res, next) => {
  const { lat, lon } = req.query;

  if (!lat || !lon) {
    return res.status(400).json({ error: true, message: "Latitude and Longitude are required" });
  }

  try {
    const response = await axios.get(
      `${REVERSE_GEO_API}?lat=${lat}&lon=${lon}&limit=1&appid=${OPENWEATHER_API_KEY}`
    );

    if (!response.data || response.data.length === 0) {
      return res.status(404).json({ error: true, message: "City not found" });
    }

    res.json({
      city: response.data[0].name,
      country: response.data[0].country,
    });
  } catch (err) {
    console.error("Reverse geocoding error:", err.message);
    next({ status: 500, message: "Failed to fetch city name" });
  }
});

// Root route
app.get("/", (req, res) => {
  res.send("Weather backend is running. Try /api/weather?city=Tokyo or /api/weather?lat=35.6&lon=139.7");
});

// Error middleware
app.use(errorHandler);

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Backend running on http://localhost:${PORT}`));
