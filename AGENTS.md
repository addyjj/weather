# Architecture

Use Clean Architecture with ports and adapters.
Organize application workflows by feature.

## Project structure

- <App>.Core: domain entities, aggregates, value objects, domain
  events, and domain services.
- <App>.UseCases: application workflows, commands, queries,
  handlers, application DTOs, and application-owned ports.
- <App>.Infrastructure: persistence and external service adapters.
- <App>.<Host>: entry points such as MCP tools, HTTP endpoints,
  or CLI commands, plus startup and dependency injection.

Create only the projects and folders the application needs.

## Dependency rules

- Core must not depend on the other application projects.
- UseCases depends on Core.
- Infrastructure implements ports defined in Core or UseCases.
- Hosts invoke use cases and wire infrastructure implementations.
- Core and UseCases must not depend on transport frameworks,
  database implementations, or external SDK models.

## Use cases

- Group each operation under its business feature.
- Use a command or query and a handler for each operation.
- Handlers coordinate domain behavior and external dependencies.
- Keep business invariants in domain objects or domain services.
- Inject handlers directly unless a mediator is justified.
- Avoid generic base handlers and abstractions without a concrete need.

Example:

UseCases/<Feature>/<Operation>/
  <Operation>Command.cs or <Operation>Query.cs
  <Operation>Handler.cs

## Ports and adapters

- A port describes an interaction needed or offered by the application.
- Express outbound ports as interfaces owned by Core or UseCases.
- Inbound adapters translate incoming requests and invoke use cases.
- Outbound adapters implement ports using storage or external systems.
- Keep adapters thin; do not place business decisions in them.

## Models

- Use domain aggregates when executing domain behavior.
- Use application-owned DTOs for read results and workflow outputs.
- Map external SDK and database models inside Infrastructure.
- Add host-specific response models only when the host needs
  a different contract.
- Do not create duplicate models or mapping layers without a reason.

## Persistence and events

- Use repositories for loading and persisting domain aggregates.
- Read queries may return DTOs directly through query ports.
- Preserve transactional consistency and handle concurrent updates.
- Add domain events when they represent meaningful business facts.
- Do not introduce event dispatch, messaging, or durable delivery
  unless the application requires it.

## Verification

- Test domain invariants and meaningful use-case behavior.
- Add adapter integration tests where external behavior matters.
- Verify at least one complete host-to-application workflow.
- Run the relevant build and tests after changes.
- Keep README commands and architecture guidance aligned with
  the actual implementation.