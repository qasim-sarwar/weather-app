export type WeatherEntry = {
  timeIso?: string;
  displayTime?: string;
  temperature?: number;
  weatherCode?: number;
  event?: string;
};

export type CurrentWeather = {
  temperature?: number;
  windspeed?: number;
  winddirection?: number;
  time?: string;
  weathercode?: number;
};

export type WeatherResponse = {
  city?: string;
  latitude?: number;
  longitude?: number;
  dayName?: string;
  minTemp?: number | null;
  minTempTime?: string | null;
  maxTemp?: number | null;
  maxTempTime?: string | null;
  current_weather?: CurrentWeather | null;
  eventForecast?: string;
  alerts?: string[];
  events?: string[];
  todayEntries?: WeatherEntry[];
  utc_offset_seconds?: number;
  timezone?: string | null;
};
