import { useState } from 'react';
import './styles/global.css';
import SearchForm from './components/SearchForm';
import WeatherCard from './components/WeatherCard';
import Spinner from './components/Spinner';
import { getWeather, getCityName } from './services/WeatherService';
import type { WeatherResponse } from './types/weather';

export default function App() {
  const [city, setCity] = useState('');
  const [latitude, setLatitude] = useState('');
  const [longitude, setLongitude] = useState('');
  const [weather, setWeather] = useState<WeatherResponse | null>(null);
  const [errorMsg, setErrorMsg] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  function resetCoordinates() {
    setLatitude('');
    setLongitude('');
  }

  function validateCityName(name: string) {
    if (!name?.trim()) {
      setErrorMsg('Please enter a valid city name.');
      return false;
    }
    if (!/^[a-zA-Z\s-]+$/.test(name.trim())) {
      setErrorMsg('City name can only contain letters, spaces, and hyphens.');
      return false;
    }
    return true;
  }

  async function fetchByCity() {
    resetCoordinates();
    setErrorMsg('');
    if (!validateCityName(city)) return;

    setIsLoading(true);
    try {
      const data = await getWeather({ city });
      setWeather(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error(msg);
      setWeather(null);
      setErrorMsg(msg || 'Error fetching weather data by city.');
    } finally {
      setIsLoading(false);
    }
  }

  async function fetchByLatLon() {
    setCity('');
    setErrorMsg('');
    if (!latitude || !longitude) {
      setErrorMsg('Please enter both latitude and longitude.');
      return;
    }

    const latNum = Number(latitude);
    const lonNum = Number(longitude);
    if (!Number.isFinite(latNum) || !Number.isFinite(lonNum)) {
      setErrorMsg('Latitude and longitude must be valid numbers.');
      return;
    }

    setIsLoading(true);
    try {
      const data = await getWeather({ lat: latNum, lon: lonNum });
      setWeather(data);
      try {
        const cityRes = await getCityName(latNum, lonNum);
        // cityRes can be object or array; handle both safely
        if (Array.isArray(cityRes) && cityRes.length > 0) {
          setCity(cityRes[0].name || '');
        } else if (cityRes && typeof cityRes === 'object' && 'name' in cityRes) {
          setCity((cityRes as { name?: string }).name ?? '');
        }
      } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e);
        console.warn('city lookup failed', msg);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error(msg);
      setWeather(null);
      setErrorMsg(msg || 'Error fetching weather data by coordinates.');
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <main className="main">
      <div className="content">
        <h1 className="subtitle">Search Today's Live Weather Forecast🌦️</h1>

        <SearchForm
          city={city}
          setCity={setCity}
          latitude={latitude}
          setLatitude={setLatitude}
          longitude={longitude}
          setLongitude={setLongitude}
          onSearchByCity={fetchByCity}
          onSearchByLatLon={fetchByLatLon}
          isLoading={isLoading}
        />

        {errorMsg && <div className="error-msg">{errorMsg}</div>}
        {isLoading && <Spinner />}

        {weather && !isLoading && <WeatherCard weather={weather} />}
      </div>
    </main>
  );
}
