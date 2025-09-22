# 🌤 Weather App
The backend is built with **Node.js + Express**, and the frontend is built with **Angular**.


A full-stack weather application built with Angular (frontend) and Node.js + Express (backend).
The backend consumes the Open-Meteo API that fetches real-time weather data using the [Open-Meteo API](https://open-meteo.com/).
 (no API key required) to fetch live weather data, while the frontend provides a simple interface for users to input latitude and longitude and view the current weather conditions.

## 🚀 Features
- Node.js backend with Express
- Weather data from Open-Meteo (no API key required)
- Angular frontend with user input for latitude & longitude
- Centralized error handling on backend
- Clean separation of backend and frontend codebases

---

## 📂 Project Structure
weather-app/
│── weather-backend/ # Node.js + Express API
│── weather-frontend/ # Angular frontend

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