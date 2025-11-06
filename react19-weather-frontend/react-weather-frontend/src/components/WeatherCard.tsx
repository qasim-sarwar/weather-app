import type { WeatherData } from '../types/weather'

type WeatherCardProps = {
  weather: WeatherData | null
}

const WeatherCard = ({ weather }: WeatherCardProps) => {
  if (!weather) {
    return (
      <div className="p-4 bg-white rounded-lg shadow text-gray-600 text-center">
        No data yet. Click “Get Weather”.
      </div>
    )
  }

  return (
    <div className="p-4 bg-white rounded-lg shadow text-center">
      <h2 className="text-2xl font-semibold mb-2">{weather.city}</h2>
      <p className="text-gray-700 mb-1">Temperature: {weather.temperature}°C</p>
      <p className="text-gray-700 mb-1">Condition: {weather.description}</p>
      <p className="text-gray-700">Humidity: {weather.humidity}%</p>
    </div>
  )
}

export default WeatherCard
