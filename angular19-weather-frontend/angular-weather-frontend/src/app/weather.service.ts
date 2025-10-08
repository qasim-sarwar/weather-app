import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class WeatherService {
  constructor(
    private http: HttpClient,
    @Inject('API_CONFIG') private config: any
  ) {}

  getWeather(city?: string, lat?: number, lon?: number) {
    const params = city
      ? `?city=${encodeURIComponent(city)}`
      : `?lat=${lat}&lon=${lon}`;

    const nodeUrl = `${this.config.nodeBaseUrl}/weather${params}`;
    const dotnetUrl = `${this.config.dotnetBaseUrl}/weather${params}`;

    return this.http.get(nodeUrl).pipe(
      catchError(err => {
        console.warn('Node backend failed, falling back to .NET:', err.message);
        return this.http.get(dotnetUrl);
      })
    );
  }

  getCityName(lat: number, lon: number) {
    const nodeUrl = `${this.config.nodeBaseUrl}/city?lat=${lat}&lon=${lon}`;
    const dotnetUrl = `${this.config.dotnetBaseUrl}/city?lat=${lat}&lon=${lon}`;

    return this.http.get(nodeUrl).pipe(
      catchError(err => {
        console.warn('Node backend failed for city lookup, using .NET fallback');
        return this.http.get(dotnetUrl);
      })
    );
  }
}
