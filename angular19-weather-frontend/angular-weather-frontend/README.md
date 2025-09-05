## Weather Frontend (Angular)📌 `weather-frontend/README.md`

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 19.2.15.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.


```markdown
# Weather Frontend (Angular)

This is an Angular application that connects to the Node.js backend and displays weather data fetched from the [Open-Meteo API](https://open-meteo.com/).

# Weather Backend (Node.js + Express)

This Frontend App connects to simple Node.js backend built with Express that fetches weather data
from the [Open-Meteo API](https://open-meteo.com/) and exposes it as a REST endpoint.

## Features
- Input latitude and longitude
- Fetches current weather from backend
- Displays temperature, wind speed, and weather code
- Uses Angular HttpClient and FormsModule

## Prerequisites
- Node.js 18+ and npm installed
- Angular CLI installed globally
  ```bash
  npm install -g @angular/cli

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

Installation
# Navigate to frontend folder
cd weather-frontend

# Install dependencies
npm install

# Running Locally
ng serve


# App will run on: http://localhost:4200

# Usage

Enter latitude and longitude in the input fields.

Click "Get Weather".

Weather data will be displayed on the page.

# Example

For Tokyo:
Latitude: 35.6895
Longitude: 139.6917
