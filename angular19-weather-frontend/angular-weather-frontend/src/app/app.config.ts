import { ApplicationConfig, provideZoneChangeDetection, importProvidersFrom } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()),
    importProvidersFrom(FormsModule),

    // Global backend API config
    {
      provide: 'API_CONFIG',
      useValue: {
        nodeBaseUrl: 'http://localhost:3000/api',
        dotnetBaseUrl: 'https://localhost:5000/api'
      }
    }
  ]
};
