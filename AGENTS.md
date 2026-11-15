# Weather repository

This monorepo contains a React dashboard, REST/SignalR API, MCP server, and weather
history importer. The frontend and backend remain independently deployable.

## Layout and scoped guidance

- `src/`: .NET application projects. Follow `src/AGENTS.md` for backend architecture.
- `tests/`: .NET tests. Follow `tests/AGENTS.md` for backend verification.
- `ui/`: React/Vite application, component tests, Storybook, and Playwright tests.
  Follow `ui/AGENTS.md` for frontend conventions.
- `docs/`: database schema and deployment guidance.
- `.github/workflows/`: shared CI and optional deployment automation.

Keep language-specific architecture rules within their respective scopes. Read the
relevant scoped guidance when editing shared configuration that affects that area.

## Shared contracts and configuration

- Coordinate REST response, SignalR event, and weather-unit changes across the API,
  UI types/services, documentation, and affected tests.
- Keep loading, empty, error, and live-update behavior consistent across the stack.
- Keep credentials in local ignored settings, User Secrets, or deployment settings.
  Browser-exposed `VITE_` variables must contain only public configuration.
- Keep local settings, dependency directories, build output, and test artifacts out
  of commits. Example configuration must use placeholders or public defaults.
- Keep README commands, relative paths, CI jobs, and deployment guidance aligned
  with the monorepo layout. Run .NET commands from the root and npm commands from `ui/`.

## Verification

- Add or update tests for meaningful behavior changes and regressions.
- Run the build and tests relevant to the changed application; use the scoped
  instructions for commands and prerequisites.
- For changes crossing the UI/API boundary, run `npm run test:e2e` from `ui/`.
  These tests start the real API and a local Ambient HTTP/Socket.IO stub.
- For documentation-only changes, check commands, paths, and the diff; application
  builds and test suites need not be rerun.
