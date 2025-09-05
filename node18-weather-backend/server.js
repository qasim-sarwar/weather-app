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

// Weather API route
app.get("/api/weather", async (req, res, next) => {
  const { lat, lon } = req.query;
  try {
    const url = `https://api.open-meteo.com/v1/forecast?latitude=${lat}&longitude=${lon}&current_weather=true`;
    const response = await axios.get(url);
    res.json(response.data);
  } catch (err) {
    console.error("Open-Meteo error:", err.message);
    next({ status: 500, message: "Failed to fetch weather data" });
  }
});

app.get("/", (req, res) => {
  res.send("Weather backend is running. Use http://localhost:3000/api/weather?lat=0&lon=0");
});

// Error middleware
app.use(errorHandler);

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Backend running on http://localhost:${PORT}`));
