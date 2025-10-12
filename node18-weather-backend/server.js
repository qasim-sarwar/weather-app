// server.js
import express from "express";
import axios from "axios";
import cors from "cors";

const app = express();
app.use(cors());
app.use(express.json());

const GEO_API = "https://geocoding-api.open-meteo.com/v1/search";
const WEATHER_API = "https://api.open-meteo.com/v1/forecast";
const NOMINATIM = "https://nominatim.openstreetmap.org/reverse";

/**
 * Ensure naive time (YYYY-MM-DDTHH:mm or YYYY-MM-DDTHH:mm:ss) becomes ISO with offset.
 * This attaches the provided offset (utc_offset_seconds) as +HH:MM or -HH:MM
 */
function formatIsoWithOffset(naiveTime, offsetSeconds) {
  if (!naiveTime) return null;
  let base = String(naiveTime);
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(base)) base = base + ":00";

  const off = Number(offsetSeconds) || 0;
  const sign = off >= 0 ? "+" : "-";
  const abs = Math.abs(off);
  const hh = Math.floor(abs / 3600).toString().padStart(2, "0");
  const mm = Math.floor((abs % 3600) / 60).toString().padStart(2, "0");

  return `${base}${sign}${hh}:${mm}`;
}

/** Return human friendly weather event(s) string and alerts array. */
function detectEvents(weatherCode, tempOrMax) {
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
    96: "Thunderstorm with hail ⛅",
    99: "Severe thunderstorm ⛈️",
  };

  const label = (weatherCode != null && map[weatherCode]) ? map[weatherCode] : (weatherCode != null ? `Code ${weatherCode}` : "Unknown");
  const alerts = [];

  if ([95, 96, 99].includes(weatherCode)) alerts.push("Severe storm risk ⚡");
  if (typeof tempOrMax === "number") {
    if (tempOrMax >= 42) alerts.push("Extreme heat 🔥🔥");
    else if (tempOrMax >= 38) alerts.push("Severe heat 🔥");
    else if (tempOrMax >= 35) alerts.push("Heatwave 🔥");
    if (tempOrMax <= -40) alerts.push("Extreme polar cold ❄️❄️");
    else if (tempOrMax <= -30) alerts.push("Extreme cold ❄️");
    else if (tempOrMax <= -20) alerts.push("Severe cold ⚠️");
    else if (tempOrMax <= -5) alerts.push("Very cold 🥶");
    else if (tempOrMax <= 0) alerts.push("Freezing ❄️");
  }

  if ([71, 73, 75, 77].includes(weatherCode)) alerts.push("Blizzard risk ❄️");
  if ([81, 82, 63, 65].includes(weatherCode)) alerts.push("Heavy precipitation / flood risk 🌧️");
  if ([45, 48].includes(weatherCode)) alerts.push("Dense Fog 🌫️");

  return {
    label,
    alerts: alerts.length ? alerts : ["No severe events detected ✅"]
  };
}

async function reverseGeocode(lat, lon) {
  try {
    const url = `${NOMINATIM}?format=json&lat=${encodeURIComponent(lat)}&lon=${encodeURIComponent(lon)}`;
    const resp = await axios.get(url, {
      headers: { "User-Agent": "node-weather-backend/1.0 (contact@example.com)" },
      timeout: 5000
    });
    const address = resp.data?.address;
    if (!address) return null;
    return address.city ?? address.town ?? address.village ?? address.state ?? null;
  } catch (err) {
    console.warn("Reverse geocode failed:", err?.message ?? err);
    return null;
  }
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
    } else {
      // If lat/lon provided but no city, try reverse geocode for a human-friendly name (non-fatal)
      if (lat && lon) {
        const name = await reverseGeocode(lat, lon);
        if (name) resolvedCity = name;
      }
    }

    if (!latitude || !longitude) {
      return res.status(400).json({ error: true, message: "Either city or lat/lon must be provided" });
    }

    // Request hourly + daily + current_weather with timezone=auto so API returns utc_offset_seconds
    const url = `${WEATHER_API}?latitude=${latitude}&longitude=${longitude}` +
      `&hourly=temperature_2m,weathercode` +
      `&daily=temperature_2m_min,temperature_2m_max,weathercode` +
      `&current_weather=true&timezone=auto`;

    const weatherResp = await axios.get(url);
    const data = weatherResp.data;

    if (!data) return res.status(500).json({ error: true, message: "No data from weather API" });

    // Pull useful fields (with safe fallbacks)
    const hourlyTimes = (data.hourly && data.hourly.time) || [];
    const hourlyTemps = (data.hourly && data.hourly.temperature_2m) || [];
    const hourlyCodes = (data.hourly && data.hourly.weathercode) || [];
    const dailyTimes = (data.daily && data.daily.time) || [];
    const dailyMinArr = (data.daily && data.daily.temperature_2m_min) || [];
    const dailyMaxArr = (data.daily && data.daily.temperature_2m_max) || [];

    const utcOffsetSeconds = (typeof data.utc_offset_seconds !== "undefined") ? Number(data.utc_offset_seconds) : 0;
    const timezone = data.timezone ?? null;

    // Determine "today" as Open-Meteo defines (daily.time[0])
    const todayDate = dailyTimes.length ? dailyTimes[0] : null;

    // Build list of hourly entries that match today's date
    let todayEntries = [];
    if (todayDate && hourlyTimes.length && hourlyTemps.length) {
      for (let i = 0; i < Math.min(hourlyTimes.length, hourlyTemps.length); i++) {
        const t = String(hourlyTimes[i]);
        if (t.startsWith(todayDate)) {
          const timeIso = formatIsoWithOffset(t, utcOffsetSeconds);
          // displayTime: use naive HH:MM -> convert to 12h with AM/PM
          const hhmm = t.split("T")[1] ?? "00:00:00";
          const [hh, mm] = hhmm.split(":");
          const h = Number(hh) % 24;
          const hour12 = ((h + 11) % 12) + 1;
          const ampm = h >= 12 ? "PM" : "AM";
          const displayTime = `${hour12}:${String(mm).padStart(2,"0")} ${ampm}`;

          todayEntries.push({
            timeIso,
            displayTime,
            temperature: Number(hourlyTemps[i]),
            weatherCode: (hourlyCodes && hourlyCodes.length > i) ? Number(hourlyCodes[i]) : null,
            event: detectEvents((hourlyCodes && hourlyCodes.length > i) ? Number(hourlyCodes[i]) : null, Number(hourlyTemps[i])).label
          });
        }
      }
    }

    // Fallback: if no today entries, use first 24 hours (best-effort)
    if (todayEntries.length === 0 && hourlyTimes.length && hourlyTemps.length) {
      const len = Math.min(24, Math.min(hourlyTimes.length, hourlyTemps.length));
      for (let i = 0; i < len; i++) {
        const t = String(hourlyTimes[i]);
        const timeIso = formatIsoWithOffset(t, utcOffsetSeconds);
        const hhmm = t.split("T")[1] ?? "00:00:00";
        const [hh, mm] = hhmm.split(":");
        const h = Number(hh) % 24;
        const hour12 = ((h + 11) % 12) + 1;
        const ampm = h >= 12 ? "PM" : "AM";
        const displayTime = `${hour12}:${String(mm).padStart(2,"0")} ${ampm}`;

        todayEntries.push({
          timeIso,
          displayTime,
          temperature: Number(hourlyTemps[i]),
          weatherCode: (hourlyCodes && hourlyCodes.length > i) ? Number(hourlyCodes[i]) : null,
          event: detectEvents((hourlyCodes && hourlyCodes.length > i) ? Number(hourlyCodes[i]) : null, Number(hourlyTemps[i])).label
        });
      }
      console.warn("No hourly entries matched today's date; using first 24 hours fallback.");
    }

    // Sort by parsed ISO (so AM -> PM)
    todayEntries.sort((a, b) => {
      const ta = a.timeIso || "";
      const tb = b.timeIso || "";
      if (!ta && !tb) return 0;
      if (!ta) return -1;
      if (!tb) return 1;
      return ta.localeCompare(tb);
    });

    // Compute min/max and their times from today's entries
    let MinTemp = null, MaxTemp = null, MinTempTime = null, MaxTempTime = null;
    if (todayEntries.length) {
      let minE = todayEntries[0], maxE = todayEntries[0];
      for (const e of todayEntries) {
        if (e.temperature < minE.temperature) minE = e;
        if (e.temperature > maxE.temperature) maxE = e;
      }
      MinTemp = minE.temperature;
      MaxTemp = maxE.temperature;
      MinTempTime = minE.timeIso;
      MaxTempTime = maxE.timeIso;
    } else {
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
        windspeed: current.windspeed != null ? Number(current.windspeed) : null,
        winddirection: current.winddirection != null ? Number(current.winddirection) : null,
        time: formatIsoWithOffset(String(current.time), utcOffsetSeconds),
        weathercode: (typeof current.weathercode !== "undefined") ? Number(current.weathercode) : null
      };
    }

    // Event detection based on current weather / maxTemp
    const detected = detectEvents(current?.weathercode ?? null, MaxTemp);

    const responsePayload = {
      latitude: Number(data.latitude ?? latitude),
      longitude: Number(data.longitude ?? longitude),
      utc_offset_seconds: utcOffsetSeconds,
      timezone: timezone,
      current_weather: current_weather,
      hourly: data.hourly ?? null,
      daily: data.daily ?? null,
      minTemp: MinTemp,
      maxTemp: MaxTemp,
      minTempTime: MinTempTime,
      maxTempTime: MaxTempTime,
      dayName: (new Date()).toLocaleDateString(undefined, { weekday: "long" }),
      city: resolvedCity,
      eventForecast: detected.label,
      alerts: detected.alerts,
      todayEntries: todayEntries
    };

    // Debug log (optional)
    // console.log("Formatted payload:", JSON.stringify(responsePayload, null, 2));

    return res.json(responsePayload);
  } catch (err) {
    console.error("Weather API error:", err?.message ?? err);
    return next({ status: 500, message: "Failed to fetch weather data" });
  }
});

app.get("/", (req, res) => {
  res.send("Weather Node backend running. Try /api/weather?city=Tokyo or /api/weather?lat=35&lon=139");
});

// Simple error handler
app.use((err, req, res, next) => {
  console.error("Unhandled error:", err);
  res.status(err.status || 500).json({ error: true, message: err.message || "Internal Server Error" });
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Node backend listening on http://localhost:${PORT}`));