import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class WeatherService {
  private apiUrl = 'https://localhost:3000/api/weather';
  
  constructor(private http: HttpClient) {}
  getCityName(lat: number, lon: number): Observable<any> {
    return this.http.get(`${this.apiUrl}?lat=${lat}&lon=${lon}&limit=1`);
  }
  getWeather(city?: string, lat?: number, lon?: number): Observable<any> {
    if (city) {
      return this.http.get(`${this.apiUrl}?city=${city}`);
    }
    const latitude = lat;
    const longitude = lon;
    return this.http.get(`${this.apiUrl}?lat=${latitude}&lon=${longitude}`);
  }
}
