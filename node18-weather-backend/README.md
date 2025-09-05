# Weather Backend (Node.js + Express)

This is a simple Node.js backend built with Express that fetches weather data
from the [Open-Meteo API](https://open-meteo.com/) and exposes it as a REST endpoint.

## Features
- `/api/weather` endpoint that takes `lat` and `lon` query parameters
- Fetches current weather data from Open-Meteo (no API key required)
- Centralized error-handling middleware
- CORS enabled for frontend requests

## Prerequisites
- Node.js 18+ and npm installed

## Installation
```bash
# Clone repository (if in monorepo, cd into backend folder)
cd weather-backend

# Install dependencies
npm install

# Running Locally
node server.js


# Server will run on: http://localhost:3000

# API Example
GET http://localhost:3000/api/weather?lat=35.6895&lon=139.6917


# Sample Response
{
  "latitude": 35.69,
  "longitude": 139.69,
  "current_weather": {
    "temperature": 27.1,
    "windspeed": 3.5,
    "winddirection": 240,
    "weathercode": 3,
    "time": "2025-09-04T07:00"
  }
}