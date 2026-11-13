# 0001 — Hand-rolled CQRS dispatcher and Result type

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

The plan calls for CQRS abstractions (`ICommand`/`IQuery` + handlers) and `Result<T>` for expected failures. MediatR, the usual choice, now requires a commercial licence for commercial use. Result libraries (FluentResults, ErrorOr) add API surface we don't need.

## Decision

- `BuildingBlocks.Application.Messaging` defines `ICommand`, `ICommand<T>`, `IQuery<T>`, their handlers and an `IDispatcher`. The dispatcher builds one typed wrapper per message type, caches it, and resolves the single handler from DI. Handlers are registered by assembly scan (`AddHandlersFromAssembly`).
- `BuildingBlocks.Domain` defines `Result`, `Result<T>` and `Error(Code, Message, ErrorType)`. `ErrorType` maps to an HTTP status in `ResultHttpExtensions.ToProblem()`.
- No pipeline behaviours yet. Cross-cutting concerns live in middleware, EF interceptors or explicit decorators when a phase needs them.

## Consequences

- No licence exposure, and roughly 150 lines of code we own.
- Adding pipeline behaviours later means extending the wrappers. That is straightforward, but it is our code to maintain.
