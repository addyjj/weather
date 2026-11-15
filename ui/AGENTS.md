# Frontend

The UI uses React, TypeScript, Vite, Tailwind CSS, TanStack Query, and SignalR.
Run the commands below from `ui/`.

## Structure and conventions

- Keep reusable presentation components under `src/components/` and page
  composition under `src/pages/`. Follow existing component folders and exports.
- Put REST calls and API URL configuration in `src/services/`, shared API types in
  `src/types/`, and data-fetching/connection hooks in `src/hooks/`.
- Use TanStack Query for REST server state. Keep SignalR connection setup, retry,
  subscriptions, and cleanup in the connection hook rather than presentation components.
- Preserve effect cleanup, including React StrictMode teardown. Stop connections
  and clear timers when hooks unmount; avoid updates after disposal.
- Preserve timestamp ordering so older live events cannot replace newer readings.
- Use existing TypeScript, ESLint, Prettier, and Tailwind conventions. Avoid adding
  abstractions or dependencies without a concrete need.
- Keep components accessible with semantic elements, labels, and appropriate
  status/alert roles. Preserve responsive layouts.
- Show explicit loading, empty, and error states. Do not fabricate a weather reading
  when no station or reading exists. Preserve the API's field names and weather units.

## API integration

- Centralize the browser API base URL in `src/services/apiConfig.ts`.
- `/api` is the default browser base URL. Vite proxies REST and SignalR requests to
  `WEATHER_API_URL` (default `http://localhost:5001`) and strips the `/api` prefix.
- `VITE_API_BASE_URL` supports a separately hosted API. It is public build-time
  configuration; Ambient credentials and SQL connections belong on the backend.
- Preserve the `/weatherHub` endpoint, `weatherData` contract, reconnect behavior,
  and `withCredentials: false` unless intentionally changing the shared contract.
- The real API project is `../src/Weather.WebApi/Weather.WebApi.csproj` from `ui/`.
  Keep Playwright's startup script and documentation aligned with that location.

## Verification

- Colocate component tests as `*.test.tsx`; use Vitest and Testing Library to test
  observable behavior. Keep stories alongside components and update affected stories.
- Keep browser workflows in `e2e/`, using the existing local Ambient stub instead of
  live credentials. Cover both proxy and cross-origin paths when changing integration.
- Run `npm ci` when dependencies need installing. Commit `package-lock.json` with
  dependency changes.
- Run `npm run build`, `npm run lint`, and `npm test` for frontend code changes.
- Run `npm run build-storybook` when changing components, stories, or Storybook config.
- Run `npm run test:e2e` for REST/SignalR, startup-path, or dashboard-flow changes.
  It requires the .NET 10 SDK, Chromium (`npx playwright install chromium`), and
  available ports 5128, 5129, 5178, and 5179.
