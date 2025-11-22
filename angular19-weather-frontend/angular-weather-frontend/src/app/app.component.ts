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
  days: Array<any> = [];
  hourlyGroups: Array<any[]> = [];
  selectedDayIndex = 0;

  errorMsg: string = '';
  events: string[] = [];

  isLoading = false;

  constructor(private weatherService: WeatherService) {}

  private extractWeatherDetails(data: any): void {
    // keep events for legacy UI and let processApiResponse build days/hourly groups
    this.events = data.events || [];
    this.processApiResponse(data);
  }

  fetchWeatherByCity(): void {
    this.resetCoordinates();
    if (!this.validateCity()) return;

    this.errorMsg = '';
    this.isLoading = true;

    this.weatherService.getWeather(this.city).subscribe({
      next: data => { this.extractWeatherDetails(data); this.isLoading = false; },
      error: err => { this.handleError(err, 'Error fetching weather data by city.'); this.isLoading = false; }
    });
  }

  fetchWeatherByLatLon(): void {
    this.city = '';
    if (!this.latitude || !this.longitude) {
      this.setError('Please enter both latitude and longitude.');
      return;
    }

    this.errorMsg = '';
    this.isLoading = true;

    this.weatherService.getWeather(undefined, this.latitude, this.longitude).subscribe({
      next: data => {
        this.extractWeatherDetails(data);
        this.fetchCityName(this.latitude!, this.longitude!);
        this.isLoading = false;
      },
      error: err => { this.handleError(err, 'Error fetching weather data by coordinates.'); this.isLoading = false; }
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

  private resetCoordinates(): void {
    this.latitude = null;
    this.longitude = null;
  }

  private setError(message: string): void {
    this.weather = null;
    this.errorMsg = message;
  }

  private handleError(err: any, defaultMsg: string): void {
    this.weather = null;
    this.errorMsg = err?.error?.error || defaultMsg;
    this.isLoading = false;
  }

  // Call this once you get the backend response (replace whatever existing processing you have)
  processApiResponse(resp: any) {
    this.weather = resp; // keep original reference if you use elsewhere

    // Build days summary from daily arrays
    const dailyTime = resp.daily?.time || [];
    const dailyMin = resp.daily?.temperature_2m_min || [];
    const dailyMax = resp.daily?.temperature_2m_max || [];
    const dailyCode = resp.daily?.weathercode || [];

    this.days = dailyTime.map((dateStr: string, idx: number) => {
      const date = new Date(dateStr + 'T00:00:00'); // safe local midnight
      const dayNameShort = date.toLocaleDateString(undefined, { weekday: 'short' }); // e.g. Sat
      const dayName = date.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
      const code = dailyCode[idx];
      const mapped = this.mapWeatherCodeToEmojiAndText(code);
      return {
        dateStr,
        dayName,
        dayNameShort,
        min: dailyMin[idx],
        max: dailyMax[idx],
        weatherCode: code,
        eventEmoji: mapped.emoji,
        eventText: mapped.text
      };
    });

    // Group hourly entries per day
    const hourlyTime = resp.hourly?.time || [];
    const hourlyTemp = resp.hourly?.temperature_2m || [];
    const hourlyCode = resp.hourly?.weathercode || [];

    // Build array of objects for each hourly index
    const hourlyEntries = hourlyTime.map((t: string, i: number) => {
      const iso = t;
      const dt = new Date(t);
      const displayTime = dt.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
      const code = hourlyCode?.[i];
      const mapped = this.mapWeatherCodeToEmojiAndText(code);
      return {
        timeIso: iso,
        displayTime,
        temperature: hourlyTemp?.[i],
        weatherCode: code,
        event: mapped.text + ' ' + mapped.emoji
      };
    });

    // Partition hourlyEntries into days using date part matching daily.time
    this.hourlyGroups = this.days.map(d => {
      return hourlyEntries.filter((h: any) => h.timeIso.startsWith(d.dateStr));
    });

    // Fallback: if first day's group is empty, try timezone-offset aware matching
    if (this.hourlyGroups[0]?.length === 0 && hourlyEntries.length) {
      // best-effort: group by ISO date string from Date object
      const grouped: { [k:string]: any[] } = {};
      hourlyEntries.forEach((h: any) => {
        const k = (new Date(h.timeIso)).toISOString().slice(0,10);
        grouped[k] = grouped[k] || [];
        grouped[k].push(h);
      });
      this.hourlyGroups = this.days.map(d => grouped[d.dateStr] || []);
    }

    // Ensure selected index stays valid
    this.selectedDayIndex = 0;
    // Optionally expose todayEntries like before
    this.weather.todayEntries = this.hourlyGroups[0] || resp.todayEntries || [];
    this.weather.dayName = this.days[0]?.dayName || this.weather.dayName;
  }

  selectDay(i: number) {
    if (i < 0 || i >= this.days.length) return;
    this.selectedDayIndex = i;
    this.weather.todayEntries = this.hourlyGroups[i] || [];
    this.weather.dayName = this.days[i]?.dayName || this.weather.dayName;
  }

  get selectedHourlyEntries() {
    return this.hourlyGroups[this.selectedDayIndex] || [];
  }

  private mapWeatherCodeToEmojiAndText(code: number) {
    // Minimal mapping for common WMO codes used in response. Extend as needed.
    switch (code) {
      case 0: return { emoji: '☀️', text: 'Clear sky' };
      case 1: return { emoji: '🌤️', text: 'Mainly clear' };
      case 2: return { emoji: '⛅', text: 'Partly cloudy' };
      case 3: return { emoji: '☁️', text: 'Overcast' };
      case 45: return { emoji: '🌫️', text: 'Fog' };
      case 48: return { emoji: '🌫️', text: 'Depositing rime fog' };
      case 51: return { emoji: '🌦️', text: 'Drizzle' };
      case 61: return { emoji: '🌧️', text: 'Rain' };
      case 71: return { emoji: '❄️', text: 'Snow' };
      case 80: return { emoji: '🌧️', text: 'Rain showers' };
      default: return { emoji: '🌤️', text: 'Weather' };
    }
  }
}