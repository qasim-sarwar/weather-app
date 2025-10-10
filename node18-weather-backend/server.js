// server.js
import express from "express";
import axios from "axios";
import cors from "cors";

const app = express();
app.use(cors());
app.use(express.json());

const GEO_API = "https://geocoding-api.open-meteo.com/v1/search";
const WEATHER_API = "https://api.open-meteo.com/v1/forecast";

/**
 * Format an Open-Meteo "naive" ISO-like time string (e.g. "2025-10-09T14:30" or "2025-10-09T14:30:00")
 * into an ISO8601 string with offset using utc_offset_seconds (e.g. "2025-10-09T14:30:00+09:00").
 */
function formatIsoWithOffset(naiveTime, offsetSeconds) {
  if (!naiveTime) return null;

  // Ensure we have seconds
  let base = naiveTime;
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(base)) base = base + ":00";
  // If already has seconds keep it

  // offsetSeconds may be undefined/null -> treat as 0
  const off = Number(offsetSeconds) || 0;
  const sign = off >= 0 ? "+" : "-";
  const abs = Math.abs(off);
  const hh = Math.floor(abs / 3600)
    .toString()
    .padStart(2, "0");
  const mm = Math.floor((abs % 3600) / 60)
    .toString()
    .padStart(2, "0");

  return `${base}${sign}${hh}:${mm}`;
}

/** Return human friendly weather event(s) string and alerts array. */
function detectEvents(weatherCode, maxTemp) {
  const map = {
    0: "Clear sky ☀️",
    1: "Mainly clear 🌤️",
    2: "Partly cloudy ⛅",
    3: "Overcast ☁️",
    45: "Fog 🌫️",
    48: "Rime fog 🌫️",
    51: "Light drizzle 🌦️",
    53: "Moderate drizzle 🌧️",
    55: "Dense drizzle 🌧️",
    61: "Slight rain 🌧️",
    63: "Moderate rain 🌧️",
    65: "Heavy rain 🌧️",
    71: "Slight snow 🌨️",
    73: "Moderate snow 🌨️",
    75: "Heavy snow ❄️",
    77: "Snow grains ❄️",
    80: "Rain showers 🌦️",
    81: "Heavy rain showers 🌧️",
    82: "Violent rain showers ⛈️",
    85: "Snow showers 🌨️",
    86: "Heavy snow showers ❄️",
    95: "Thunderstorm ⛈️",
    96: "Thunderstorm with hail ⛈️",
    99: "Severe thunderstorm ⛈️",
  };

  const label = weatherCode != null && map[weatherCode] ? map[weatherCode] : (weatherCode != null ? `Code ${weatherCode}` : "Unknown");
  const alerts = [];

  if ([95, 96, 99].includes(weatherCode)) alerts.push("Thunderstorm ⚡");
  if (typeof maxTemp === "number" && maxTemp >= 35) alerts.push("Heatwave 🔥");
  if (typeof maxTemp === "number" && maxTemp <= -5) alerts.push("Blizzard ❄️");
  if ([45, 48].includes(weatherCode)) alerts.push("Dense Fog 🌫️");
  if ([63, 65, 81, 82].includes(weatherCode)) alerts.push("Heavy Rain / Flood Risk 🌧️");
  // Add more rules as needed

  return {
    label,
    alerts: alerts.length ? alerts : ["No severe events detected ✅"]
  };
}

app.get("/api/weather", async (req, res, next) => {
  const { city, lat, lon } = req.query;

  try {
    let latitude = lat;
    let longitude = lon;
    let resolvedCity = city ?? null;

    // Resolve city -> coordinates (if necessary)
    if (city) {
      const geoRes = await axios.get(`${GEO_API}?name=${encodeURIComponent(city)}&count=1`);
      const geoData = geoRes.data;
      if (!geoData?.results || geoData.results.length === 0) {
        return res.status(404).json({ error: true, message: "City not found" });
      }
      const r = geoData.results[0];
      latitude = r.latitude;
      longitude = r.longitude;
      resolvedCity = r.name ?? resolvedCity;
    }

    if (!latitude || !longitude) {
      return res.status(400).json({ error: true, message: "Either city or lat/lon must be provided" });
    }

    // Request hourly + daily + current_weather with timezone=auto so API returns utc_offset_seconds
    const url = `${WEATHER_API}?latitude=${latitude}&longitude=${longitude}` +
      `&hourly=temperature_2m&daily=temperature_2m_min,temperature_2m_max,weathercode&current_weather=true&timezone=auto`;

    const weatherResp = await axios.get(url);
    const data = weatherResp.data;

    if (!data) return res.status(500).json({ error: true, message: "No data from weather API" });

    // Pull useful fields (with safe fallbacks)
    const hourlyTimes = (data.hourly && data.hourly.time) || [];
    const hourlyTemps = (data.hourly && data.hourly.temperature_2m) || [];
    const dailyTimes = (data.daily && data.daily.time) || [];
    const dailyMinArr = (data.daily && data.daily.temperature_2m_min) || [];
    const dailyMaxArr = (data.daily && data.daily.temperature_2m_max) || [];

    const utcOffsetSeconds = typeof data.utc_offset_seconds !== "undefined" ? Number(data.utc_offset_seconds) : null;
    const timezone = data.timezone ?? null;

    // Determine "today" as Open-Meteo defines (daily.time[0])
    const todayDate = dailyTimes.length ? dailyTimes[0] : null;

    // Build list of hourly entries that match today's date
    const todayEntries = [];
    if (todayDate && hourlyTimes.length && hourlyTemps.length) {
      for (let i = 0; i < Math.min(hourlyTimes.length, hourlyTemps.length); i++) {
        const t = String(hourlyTimes[i]);
        if (t.startsWith(todayDate)) {
          todayEntries.push({ time: t, temp: Number(hourlyTemps[i]), idx: i });
        }
      }
    }

    // Fallback: if no today entries, use first 24 hours (best-effort)
    if (todayEntries.length === 0 && hourlyTimes.length && hourlyTemps.length) {
      for (let i = 0; i < Math.min(24, Math.min(hourlyTimes.length, hourlyTemps.length)); i++) {
        todayEntries.push({ time: String(hourlyTimes[i]), temp: Number(hourlyTemps[i]), idx: i });
      }
      console.warn("No hourly entries matched today's date; using first 24 hours fallback.");
    }

    // Compute min/max and their times from today's entries
    let MinTemp = null, MaxTemp = null, MinTempTime = null, MaxTempTime = null;
    if (todayEntries.length) {
      let minE = todayEntries[0], maxE = todayEntries[0];
      for (const e of todayEntries) {
        if (e.temp < minE.temp) minE = e;
        if (e.temp > maxE.temp) maxE = e;
      }
      MinTemp = minE.temp;
      MaxTemp = maxE.temp;
      MinTempTime = formatIsoWithOffset(minE.time, utcOffsetSeconds);
      MaxTempTime = formatIsoWithOffset(maxE.time, utcOffsetSeconds);
    } else {
      // fallback to daily arrays without times
      MinTemp = dailyMinArr.length ? Number(dailyMinArr[0]) : null;
      MaxTemp = dailyMaxArr.length ? Number(dailyMaxArr[0]) : null;
      MinTempTime = null;
      MaxTempTime = null;
    }

    // Normalize current_weather and attach offset-aware time string
    const current = data.current_weather ?? null;
    let current_weather = null;
    if (current) {
      current_weather = {
        temperature: Number(current.temperature),
        windspeed: Number(current.windspeed),
        winddirection: Number(current.winddirection),
        time: formatIsoWithOffset(String(current.time), utcOffsetSeconds),
        weathercode: typeof current.weathercode !== "undefined" ? Number(current.weathercode) : null,
      };
    }

    // Event detection
    const { label: eventLabel, alerts } = detectEvents(current?.weathercode ?? null, MaxTemp);

    // Compose final object matching your .NET WeatherForecast shape + extras
    const formatted = {
      latitude: Number(data.latitude ?? latitude),
      longitude: Number(data.longitude ?? longitude),
      utc_offset_seconds: utcOffsetSeconds,
      timezone: timezone,
      current_weather: current_weather,
      hourly: data.hourly ?? null,
      daily: data.daily ?? null,
      MinTemp,
      MaxTemp,
      MinTempTime,
      MaxTempTime,
      EventForecast: eventLabel,
      alerts: alerts,
      // Optional: also provide a compact 'current' block if some clients expect it
      current: current_weather ? {
        temperature: current_weather.temperature,
        windspeed: current_weather.windspeed,
        winddirection: current_weather.winddirection,
        time: current_weather.time,
        weathercode: current_weather.weathercode,
        event: eventLabel
      } : null
    };

    // debug - remove in production if noisy
    console.log("Formatted weather payload:", JSON.stringify(formatted, null, 2));

    return res.json(formatted);
  } catch (err) {
    console.error("Weather API error:", err?.message ?? err);
    return next({ status: 500, message: "Failed to fetch weather data" });
  }
});

app.get("/", (req, res) => {
  res.send("Weather Node backend running. Try /api/weather?city=Tokyo");
});

// Simple error handler
app.use((err, req, res, next) => {
  console.error("Unhandled error:", err);
  res.status(err.status || 500).json({ error: true, message: err.message || "Internal Server Error" });
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Node backend listening on http://localhost:${PORT}`));
