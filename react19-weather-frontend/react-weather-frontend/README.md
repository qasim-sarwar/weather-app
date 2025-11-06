# weather-station-ts

React + TypeScript weather frontend built with Vite. Uses native `fetch`, Tailwind optional for styling, MSW for mocking requests in development and tests, and Vitest for unit/integration tests. GitHub Actions CI included.

## Features

- Vite + React + TypeScript
- No axios, uses native `fetch`
- Vitest + React Testing Library for tests
- Environment-based backend config via `.env` (VITE_NODE_BASE_URL, VITE_DOTNET_BASE_URL)