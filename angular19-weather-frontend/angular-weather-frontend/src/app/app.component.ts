import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { WeatherService } from './weather.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'] // <-- Correct property name
})
export class AppComponent {
  title = 'angular-weather-frontend';

  latitude: number | null = null;
  longitude: number | null = null;
  weather: any;
  errorMsg: string = '';

  constructor(private weatherService: WeatherService) {}

  fetchWeatherByLatLon() {
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
        this.errorMsg = 'Error fetching weather data.';
        console.error('Error fetching weather', err);
      }
    });
  }
}
