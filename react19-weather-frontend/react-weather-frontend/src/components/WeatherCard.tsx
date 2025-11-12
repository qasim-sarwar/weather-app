import type { WeatherResponse } from '../types/weather';

export default function WeatherCard({ weather }: { weather: WeatherResponse | null }) {
  if (!weather) {
    return <div className="weather-result"><div className="weather-card">No data yet. Use the form above to fetch weather.</div></div>;
  }

  return (
    <div className="weather-result">
      <h3>
        Current Weather of {weather.city ? weather.city : `(${weather.latitude}, ${weather.longitude})`} <span>🕓 On: {weather.dayName ?? ''}</span>
      </h3>

      <div className="card-container">
        {typeof weather.minTemp !== 'undefined' && weather.minTemp !== null && (
          <div className="weather-card">
            <h4>🌡️ Min Temperature {weather.minTemp} °C</h4>
            <p><strong>🕓 At:</strong> {weather.minTempTime
    ? new Date(weather.minTempTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : '-'}</p>
          </div>
        )}

        {weather.current_weather?.temperature !== undefined && weather.current_weather !== null && (
          <div className="weather-card">
            <h4>☀️ Current Weather {weather.current_weather.temperature} °C</h4>
            <p><strong>⚠️ {weather.eventForecast ?? ''}</strong></p>
            <p><strong>💨 Wind speed:</strong> {weather.current_weather.windspeed ?? '-'} km/h</p>
            <p><strong>🧭 Wind Direction:</strong> {weather.current_weather.winddirection ?? '-'}°</p>

            {weather.events && weather.events.length > 0 && (
              <div className="event-section">
                <h4>🌦️ Forecasted Events</h4>
                <ul>
                  {weather.events.map((ev, i) => <li key={i}>{ev}</li>)}
                </ul>
              </div>
            )}
          </div>
        )}

        {typeof weather.maxTemp !== 'undefined' && weather.maxTemp !== null && (
          <div className="weather-card">
            <h4>🔥 Max Temperature {weather.maxTemp} °C</h4>
            <p><strong>🕓 At:</strong> {weather.maxTempTime
    ? new Date(weather.maxTempTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : '-'}</p>
          </div>
        )}
      </div>

      {Array.isArray(weather.todayEntries) && weather.todayEntries.length > 0 && (
        <>
          <h4 style={{ textAlign: 'center', marginTop: '1rem' }}>Today's Hourly Forecast of {weather.city ?? ''} 🌦️</h4>
          <div className="hourly-cards-grid">
            {weather.todayEntries.map((e, i) => (
              <div className="hour-card" key={i}>
                <div className="hour-top">🕓 {e.displayTime ?? e.timeIso}</div>
                <div className="hour-temp">🌡️ {e.temperature} °C</div>
                <div className="hour-event">{e.event}</div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
