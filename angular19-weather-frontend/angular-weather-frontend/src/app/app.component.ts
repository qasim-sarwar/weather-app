import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { WeatherService } from './weather.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent {
  title = 'angular-weather-frontend';

  latitude: number | null = null;
  longitude: number | null = null;
  city: string = '';

  weather: any = null;
  errorMsg: string = '';

  minTemp: number | null = null;
  maxTemp: number | null = null;
  currentTemp: number | null = null;
  events: string[] = [];

  constructor(private weatherService: WeatherService) {}

  /** Extracts relevant weather details and updates component state */
  private extractWeatherDetails(data: any): void {
    this.weather = data;
    this.currentTemp = data.current_weather?.temperature ?? null;
    this.minTemp = data.daily?.temperature_2m_min?.[0] ?? null;
    this.maxTemp = data.daily?.temperature_2m_max?.[0] ?? null;
    this.events = this.getWeatherEvents(data.daily?.weathercode ?? []);
  }

  /** Maps weather codes to human-readable events */
  private getWeatherEvents(codes: number[]): string[] {
    const codeEventMap: Record<number, string> = {
      0: '☀️ Clear Sky',
      2: '💨 Windy', 3: '💨 Windy',
      45: '🌫️ Dense Fog', 48: '🌫️ Dense Fog',
      51: '🌧️ Rain', 61: '🌧️ Rain', 63: '🌧️ Rain',
      72: '🌨️ Snowfall', 77: '🌨️ Snowfall',
      85: '❄️ Blizzard', 86: '❄️ Blizzard',
      95: '⛈️ Thunderstorm', 96: '⛈️ Thunderstorm', 99: '⛈️ Thunderstorm'
    };

    return [...new Set(codes.map(code => codeEventMap[code]).filter(Boolean))];
  }

  /** Fetch weather by city name */
  fetchWeatherByCity(): void {
    this.resetCoordinates();

    if (!this.validateCity()) return;

    this.errorMsg = '';
    this.weatherService.getWeather(this.city).subscribe({
      next: data => this.extractWeatherDetails(data),
      error: err => this.handleError(err, 'Error fetching weather data by city.')
    });
  }

  /** Fetch weather by latitude and longitude */
  fetchWeatherByLatLon(): void {
    this.city = '';
    if (!this.latitude || !this.longitude) {
      this.setError('Please enter both latitude and longitude.');
      return;
    }

    this.errorMsg = '';
    this.weatherService.getWeather(undefined, this.latitude, this.longitude).subscribe({
      next: data => {
        this.extractWeatherDetails(data);
        this.fetchCityName(this.latitude!, this.longitude!);
      },
      error: err => this.handleError(err, 'Error fetching weather data by coordinates.')
    });
  }

  private fetchCityName(lat: number, lon: number): void {
  this.weatherService.getCityName(lat, lon).subscribe({
    next: (res: any) => {
      if (Array.isArray(res) && res.length > 0) {
        this.city = res[0].name;
      } else if (res?.name) {
        this.city = res.name;
      }
    },
    error: err => console.error('Error fetching city name:', err)
  });
}

  /** Validate city input */
  private validateCity(): boolean {
    if (!this.city?.trim()) {
      this.setError('Please enter a valid city name.');
      return false;
    }
    if (!/^[a-zA-Z\s\-]+$/.test(this.city.trim())) {
      this.setError('City name can only contain letters, spaces, and hyphens.');
      return false;
    }
    return true;
  }

  /** Reset coordinates */
  private resetCoordinates(): void {
    this.latitude = null;
    this.longitude = null;
  }

  /** Set error state */
  private setError(message: string): void {
    this.weather = null;
    this.errorMsg = message;
  }

  /** Handle HTTP errors */
  private handleError(err: any, defaultMsg: string): void {
    this.weather = null;
    this.errorMsg = err?.error?.error || defaultMsg;
  }
}
