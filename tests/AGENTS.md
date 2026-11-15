# Backend tests

Read `../src/AGENTS.md` for the backend architecture and project boundaries when
changing .NET tests. Tests live in `Weather.Tests` and use xUnit.

## Test behavior

- Test domain invariants and meaningful use-case behavior. Add adapter integration
  tests where external behavior matters, and preserve complete host-to-application
  workflows through the production MCP, REST, and import entry points.
- Use local Ambient stubs rather than live credentials or production services.
- SQL integration tests require Windows SQL Server Express LocalDB (`MSSQLLocalDB`).
  Use the existing disposable `HistoryDatabase` fixture and uniquely named test
  databases. Never point tests at a configured production or personal weather database.
- Keep LocalDB failures explicit; do not silently skip tests or substitute in-memory
  storage for SQL behavior.
- Preserve coverage of transaction rollback, concurrent inserts, cancellation,
  pagination, timezone/DST behavior, and sanitized errors when changing those paths.
- Keep timeouts bounded and dispose test servers, processes, and databases.

## Commands

Run from the repository root:

```powershell
dotnet build Weather.slnx -c Release
dotnet test Weather.slnx -c Release --no-build
```

Optional line and branch coverage, including child host processes:

```powershell
dotnet test Weather.slnx -c Release --no-build --collect 'Code Coverage;Format=Cobertura' --results-directory TestResults
```

UI component and browser tests live under `ui/` and follow `ui/AGENTS.md`.
