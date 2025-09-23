# 🌤 Weather App
The backend is built with **Node.js + Express**, and the frontend is built with **Angular**.


A full-stack weather application built with Angular (frontend) and Node.js + Express (backend).
The backend consumes the Open-Meteo API that fetches real-time weather data using the [Open-Meteo API](https://open-meteo.com/).
 (no API key required) to fetch live weather data, while the frontend provides a simple interface for users to input latitude and longitude and view the current weather conditions.

## 🚀 Features
- Search weather by **city name**
- Search weather by **latitude and longitude**
- Responsive design
- Middleware in .NET backend to **rate-limit abusive requests**:
  - 10,000 requests per day
  - 5,000 requests per hour
  - 600 requests per minute
- Node.js backend with Express
- Weather data from Open-Meteo (no API key required)
- Angular frontend with user input for latitude & longitude
- Centralized error handling on backend
- Clean separation of backend and frontend codebases

## 📂 Project Structure
weather-app/
├── angular-frontend/     # Angular application (UI)
├── node-backend/         # Node.js + Express backend
├── dotnet-backend/       # .NET 8 Minimal API backend
└── README.md             # Project documentation


🌤 Weather App – Angular + Node.js

A full-stack weather application built with Angular (frontend) and Node.js + Express (backend).
The backend consumes the Open-Meteo API
 (no API key required) to fetch live weather data, while the frontend provides a simple interface for users to input latitude and longitude and view the current weather conditions.

Key Highlights:

Backend (Node.js + Express):

REST API endpoint /api/weather

Fetches weather data from Open-Meteo

Centralized error handling middleware

CORS enabled for cross-origin requests

Frontend (Angular):

Input fields for latitude and longitude

Calls backend API and displays temperature, wind speed, and weather description

Uses Angular HttpClient and FormsModule

This project demonstrates a clean separation of frontend and backend, API consumption with error handling, and integration of modern frameworks for building scalable web apps.

1. Angular Frontend

The frontend is built with the latest Angular and consumes the weather backend APIs.

Setup & Run
cd angular-frontend
npm install
ng serve --open


The app will run at:
👉 http://localhost:4200

2. Node.js Backend

A simple Express server that fetches weather data from an external API and exposes it at /api/weather.

Setup & Run
cd node-backend
npm install
node server.js


The backend will run at:
👉 http://localhost:3000/api/weather

3. .NET Backend (Minimal API)

A .NET 8 Minimal API alternative backend that returns weather forecast data.

Setup & Run
cd dotnet-backend
dotnet restore
dotnet run


The backend will run at:
👉 http://localhost:3000/api/weather

(Port is set to 3000 to align with the Angular frontend service expectations.)

4. Choosing a Backend

The Angular frontend is preconfigured to call:

http://localhost:3000/api/weather


So you can run either the Node backend or the .NET backend, depending on preference.

5. GitHub Setup

This repository contains both frontend and backend code. To contribute:

git clone https://github.com/qasim-sarwar/weather-app.git
cd weather-app

6. Future Enhancements

Add user location-based weather lookup.

Deploy Angular + Backend to cloud (Azure, AWS, or Vercel).

Replace mock .NET data with live weather API integration.