import { apiConfig } from '../config/apiConfig';
import type { WeatherResponse } from '../types/weather';

type CityResponse = {
  name?: string;
  country?: string;
  [key: string]: unknown;
};

const sanitize = (u: string) => u.replace(/\/$/, '');

async function fetchJson<T = unknown>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

/**
 * Try node backend first, then fallback to dotnet.
 * params: { city?: string; lat?: number; lon?: number }
 */
export async function getWeather(params: { city?: string; lat?: number; lon?: number }): Promise<WeatherResponse> {
  const q = params.city ? `?city=${encodeURIComponent(params.city)}` : `?lat=${params.lat}&lon=${params.lon}`;
  const node = apiConfig.nodeBaseUrl ? `${sanitize(apiConfig.nodeBaseUrl)}/weather${q}` : null;
  const dot = apiConfig.dotnetBaseUrl ? `${sanitize(apiConfig.dotnetBaseUrl)}/weather${q}` : null;

  if (node) {
    try {
      return await fetchJson<WeatherResponse>(node);
    } catch (err) {
      console.warn('Node backend failed, falling back to .NET', (err as Error).message);
    }
  }
  if (dot) return fetchJson<WeatherResponse>(dot);

  // fallback to relative path (dev proxy)
  return fetchJson<WeatherResponse>(`/api/weather${q}`);
}

export async function getCityName(lat: number, lon: number): Promise<CityResponse> {
  const q = `?lat=${lat}&lon=${lon}`;
  const node = apiConfig.nodeBaseUrl ? `${sanitize(apiConfig.nodeBaseUrl)}/city${q}` : null;
  const dot = apiConfig.dotnetBaseUrl ? `${sanitize(apiConfig.dotnetBaseUrl)}/city${q}` : null;

  if (node) {
    try {
      return await fetchJson<CityResponse>(node);
    } catch (err) {
      console.warn('Node city lookup failed, falling back to .NET', (err as Error).message);
    }
  }
  if (dot) return fetchJson<CityResponse>(dot);

  return fetchJson<CityResponse>(`/api/city${q}`);
}
