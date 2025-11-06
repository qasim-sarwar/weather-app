import React, { useState } from 'react';
import './styles/global.css';

function App() {
  const [city, setCity] = useState('');
  const [weather, setWeather] = useState(null);

  const getWeather = async () => {
    if (!city) return;
    const res = await fetch(`http://localhost:3000/api/weather?city=${city}`);
    const data = await res.json();
    setWeather(data);
  };

  return (
    <div className="main">
      <h1>Weather Dashboard</h1>
      <p>Check current weather by city name</p>

      <div className="weather-form">
        <input
          type="text"
          className="weather-input"
          placeholder="Enter city name"
          value={city}
          onChange={(e) => setCity(e.target.value)}
        />
        <button className="weather-btn" onClick={getWeather}>
          Get Weather
        </button>
      </div>

      {weather && (
        <div className="weather-result">
          <h3>{weather.city}</h3>
          <p>Temperature: {weather.temp}°C</p>
          <p>Condition: {weather.condition}</p>
        </div>
      )}
    </div>
  );
}

export default App;
