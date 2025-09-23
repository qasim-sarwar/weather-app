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

  weather: any;
  errorMsg: string = '';

  constructor(private weatherService: WeatherService) {}

  fetchWeatherByCity() {
    // Reset latitude and longitude when searching by city
    this.latitude = null;
    this.longitude = null;

    if (!this.city || this.city.trim().length === 0) {
      this.weather = null;
      this.errorMsg = 'Please enter a valid city name.';
      return;
    }

    // Only allow letters, spaces, and hyphens
    if (!/^[a-zA-Z\s\-]+$/.test(this.city.trim())) {
      this.weather = null;
      this.errorMsg = 'City name can only contain letters, spaces, and hyphens.';
      return;
    }

    this.errorMsg = '';
    this.weatherService.getWeather(this.city).subscribe({
      next: (data) => (this.weather = data),
      error: (err) => {
        this.weather = null;
        this.errorMsg = 'Error fetching weather data for city.';
        console.error('Error fetching weather by city', err);
      }
    });
  }

  fetchWeatherByLatLon() {
    // Reset city when searching by lat/lon
    this.city = '';

    if (this.latitude == null || this.longitude == null) {
      this.weather = null;
      this.errorMsg = 'Please enter both latitude and longitude.';
      return;
    }

    this.errorMsg = '';
    this.weatherService.getWeather(undefined, this.latitude, this.longitude).subscribe({
      next: (data) => (this.weather = data),
      error: (err) => {
        this.weather = null;
        this.errorMsg = 'Error fetching weather data by coordinates.';
        console.error('Error fetching weather by lat/lon', err);
      }
    });
  }
}
