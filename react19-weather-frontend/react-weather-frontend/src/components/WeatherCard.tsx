import { useMemo, useState } from 'react';
import type { WeatherResponse, WeatherEntry } from '../types/weather';

const weatherCodeMap: Record<number, { icon: string; text: string }> = {
  0: { icon: '☀️', text: 'Clear sky' },
  1: { icon: '🌤️', text: 'Mainly clear' },
  2: { icon: '⛅', text: 'Partly cloudy' },
  3: { icon: '☁️', text: 'Cloudy' },
  45: { icon: '🌫️', text: 'Fog' },
  48: { icon: '🌫️', text: 'Rime fog' },
  51: { icon: '🌦️', text: 'Light drizzle' },
  61: { icon: '🌧️', text: 'Rain' },
  71: { icon: '🌨️', text: 'Snow' },
  80: { icon: '🌦️', text: 'Showers' },
  95: { icon: '⛈️', text: 'Thunderstorm' },
};

function getCodeInfo(code?: number) {
  if (typeof code !== 'number') return { icon: '❔', text: '' };
  return weatherCodeMap[code] ?? { icon: '❔', text: '' };
}

export default function WeatherCard({ weather }: { weather: WeatherResponse | null }) {
  // hooks must be called unconditionally — compute safe fallbacks from possible null `weather`
  const days = weather?.daily?.time ?? [];
  const [selectedDayIndex, setSelectedDayIndex] = useState<number>(0);
  const selectedDate = days[selectedDayIndex] ?? new Date().toISOString().slice(0, 10);

  // build hourly entries for the selected day from weather.hourly arrays if present,
  // otherwise fallback to filtering todayEntries
  const hourlyEntries: WeatherEntry[] = useMemo(() => {
    const out: WeatherEntry[] = [];

    if (!weather) return out;

    if (weather.hourly?.time && weather.hourly.time.length > 0) {
      const times = weather.hourly.time;
      const temps = weather.hourly.temperature_2m ?? [];
      const codes = weather.hourly.weathercode ?? [];

      for (let j = 0; j < times.length; j++) {
        const t = times[j];
        // times from API look like "YYYY-MM-DDTHH:MM" - match date prefix
        if (t.startsWith(selectedDate)) {
          const displayTime = (() => {
            try {
              const d = new Date(t);
              return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
            } catch {
              return t.split('T')[1] ?? t;
            }
          })();

          out.push({
            timeIso: t,
            displayTime,
            temperature: typeof temps[j] !== 'undefined' ? temps[j] : undefined,
            weatherCode: typeof codes[j] !== 'undefined' ? codes[j] : undefined,
            // keep any existing event undefined here; we will render event text using weatherCode map if needed
          });
        }
      }
    } else if (weather.todayEntries && weather.todayEntries.length > 0) {
      // fallback: filter entries that belong to selectedDate (todayEntries contain full iso with timezone)
      for (const e of weather.todayEntries) {
        if (e.timeIso && e.timeIso.includes(selectedDate)) {
          out.push(e);
        }
      }
    }

    // Ensure ordering by time and limit to 24 if more
    out.sort((a, b) => {
      const ta = a.timeIso ? new Date(a.timeIso).getTime() : 0;
      const tb = b.timeIso ? new Date(b.timeIso).getTime() : 0;
      return ta - tb;
    });

    return out.slice(0, 24);
  }, [weather, selectedDate]);

  // early return after hooks are declared
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

      {/* Horizontal day selector (centered) */}
      {days.length > 0 && (
        <>
          <div className="day-selector">
            {days.map((dateStr: string, i: number) => {
              const shortName = new Date(dateStr).toLocaleDateString(undefined, { weekday: 'short' });
              const isSelected = i === selectedDayIndex;
              const code = weather.daily?.weathercode?.[i];
              const codeInfo = getCodeInfo(code);
              const min = weather.daily?.temperature_2m_min?.[i];
              const max = weather.daily?.temperature_2m_max?.[i];

              return (
                <button
                  key={dateStr}
                  onClick={() => setSelectedDayIndex(i)}
                  className={`day-button ${isSelected ? 'selected' : ''}`}
                  aria-pressed={isSelected}
                >
                  <div className="day-short">{shortName} {codeInfo.icon}</div>
                  <div className="day-date">{new Date(dateStr).toLocaleDateString()}</div>
                  <div className="day-temp">{typeof min !== 'undefined' ? `${min}°` : '-'} / {typeof max !== 'undefined' ? `${max}°` : '-'}</div>
                </button>
              );
            })}
          </div>

          <h4 className="section-title">Hourly Weather of {new Date(selectedDate).toLocaleDateString()}</h4>
          {/* 24 hourly cards in grid (3 rows x 8 columns) */}
          <div className="hourly-cards-grid">
            {hourlyEntries.length === 0 && <div className="no-hourly">No hourly data for this day.</div>}
            {hourlyEntries.map((entry: WeatherEntry, idx: number) => {
              const codeInfo = getCodeInfo(entry.weatherCode);
              const eventText = entry.event ?? codeInfo.text;
              return (
                <div key={entry.timeIso ?? idx} className="hour-card">
                  <div className="hour-top">🕓 {entry.displayTime ?? new Date(entry.timeIso ?? '').toLocaleTimeString([], { hour: 'numeric' })} {codeInfo.icon}</div>
                  <div className="hour-temp">🌡️ {typeof entry.temperature !== 'undefined' ? `${entry.temperature} °C` : '-'}</div>
                  <div className="hour-event">{eventText}</div>
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
}
