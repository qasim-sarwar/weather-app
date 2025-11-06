import { apiConfig } from '../config/apiConfig';

export async function fetchWeather(lat: number, lon: number) {
  const url = `${apiConfig.nodeBaseUrl}/weather?lat=${lat}&lon=${lon}`;

  try {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`Node API failed with status: ${response.status}`);
    return await response.json();
  } catch (error) {
    console.warn('Falling back to .NET API:', error);
    const dotnetUrl = `${apiConfig.dotnetBaseUrl}/weather?lat=${lat}&lon=${lon}`;
    const fallbackResponse = await fetch(dotnetUrl);
    if (!fallbackResponse.ok) throw new Error('Both APIs failed');
    return await fallbackResponse.json();
  }
}
