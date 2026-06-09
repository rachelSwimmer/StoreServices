# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the entire solution
dotnet build StoreServices.sln

# Build a single service
dotnet build src/OrderService/OrderService.csproj

# Run a specific service locally
dotnet run --project src/UserAuthService
dotnet run --project src/ProductCatalogService
dotnet run --project src/OrderService
dotnet run --project src/BffService
dotnet run --project src/ApiGateway

# Run all services + infrastructure (SQL Server on port 1434, Redis on port 6380)
docker compose up --build
```

## Tests

The `tests/` directory is currently empty — no test projects exist yet.

## EF Core Migrations

```bash
# Add a migration (run from repo root)
dotnet ef migrations add <MigrationName> --project src/UserAuthService --startup-project src/UserAuthService
dotnet ef migrations add <MigrationName> --project src/ProductCatalogService --startup-project src/ProductCatalogService
dotnet ef migrations add <MigrationName> --project src/OrderService --startup-project src/OrderService

# Migrations are applied automatically on startup via db.Database.Migrate()
```

## Architecture

This is a .NET 8 microservices solution with six services and a shared library:

| Project | Port (local) | Port (Docker) | Database |
|---|---|---|---|
| ApiGateway | 5000 | 8082 | — |
| UserAuthService | 5019 | 8080 | UserAuthDb |
| ProductCatalogService | 5149 | 8081 | CatalogDb |
| OrderService | 5150 | 8083 | OrderDb |
| BffService | 5151 | 8084 | — |
| NotificationService | — | 8086 | — |

Infrastructure: SQL Server 2022 (port 1434 locally / 1433 in Docker), Redis (port 6380 locally / 6379 in Docker), RabbitMQ (ports 5672 AMQP + 15672 management UI; same in Docker), and `grafana/otel-lgtm` (Docker only) as the OpenTelemetry backend — Grafana UI on port 3000, OTLP ingest on 4317 (gRPC) / 4318 (HTTP).

In Docker, ProductCatalogService runs as three replicas (`product-catalog-1/2/3`, debug ports 8091-8093) behind an Nginx load balancer (`nginx-catalog-lb`, port 8085) — see "Load balancing" below. Locally it runs as a single instance.

### Request flow

All external traffic enters through **ApiGateway**, which uses **Ocelot** as a reverse proxy. Routes are defined in `src/ApiGateway/appsettings.json`. The gateway validates JWT tokens before forwarding most routes; `/api/auth/*` is unauthenticated. Redis-backed rate limiting middleware in SharedKernel is applied at both the gateway and service level (skipped gracefully when Redis is unavailable).

### Service-to-service communication

**OrderService** calls the other two services directly over HTTP — not through the gateway. These calls go to `internal/` endpoints that are **not JWT-protected** and not exposed through the gateway:

- `GET internal/users/{id}/exists` and `GET internal/users/{id}/summary` on UserAuthService
- `GET internal/products/{id}/price-and-stock`, `POST internal/products/reserve-stock`, `POST internal/products/release-reservation` on ProductCatalogService

The target URLs are configured in `appsettings.json` under `Services:UserAuthService` and `Services:CatalogService`.

### BffService (API composition)

**BffService** is a backend-for-frontend that aggregates data from multiple services into a single response. Its one route, `GET /api/composed/orders/{id}` (`ComposedController`, JWT-protected, exposed through the gateway), is handled by `OrderDetailComposer`, which fans out to OrderService, UserAuthService, and CatalogService via typed HTTP clients in `Clients/`. Downstream client interfaces (`ICatalogClient`, `IUserClient`) have `Cached*` decorator implementations mirroring the ProductCatalogService caching pattern. The composer is failure-tolerant: if a downstream call fails, that section of the view is returned as null rather than failing the whole request.

### Stock reservation saga

When `OrderService` creates an order it calls `ReserveStock` on each item. If anything fails mid-loop it releases all already-acquired reservations (`ReleaseReservation`) as a compensation step. Reservations have a 5-minute TTL and a `ReservationSweeperService` background worker in ProductCatalogService sweeps expired ones every 60 seconds.

### Async messaging (RabbitMQ)

In addition to the synchronous HTTP calls above, there is one **event-driven** flow as a teaching example. When `OrderService` finishes creating an order it publishes an `OrderCreated` event to a RabbitMQ **topic exchange** (`store.events`, routing key `order.created`). **NotificationService** — a consumer-only service with no HTTP API or DB — binds a queue (`notifications.order-created`) with pattern `order.*` and "sends" a confirmation (logs it). Publishing is fire-and-forget: a broker outage never fails order creation.

Built on the raw `RabbitMQ.Client` library (not MassTransit) to keep AMQP mechanics visible. Key design point: the `OrderCreatedEvent` contract is **duplicated** in each service (`src/OrderService/Messaging/` and `src/NotificationService/Messaging/`), not shared — services agree only on the JSON wire shape ("tolerant reader"), preserving independent deployability. Only reusable plumbing (`IEventPublisher`/`RabbitMqPublisher`, `MessagingTopology` names, `RabbitMqSettings`) lives in SharedKernel. Connection config is the `RabbitMq` section in `appsettings.json` (overridden by `RabbitMq__HostName: rabbitmq` in Docker). Full walkthrough: `docs/rabbitmq-example.md`.

### Observability (OpenTelemetry — traces, metrics, logs)

All three pillars are wired via **OpenTelemetry**, exporting over **OTLP** to the `grafana/otel-lgtm` container (Tempo + Prometheus + Loki + Grafana in one image). The OTel packages live in **SharedKernel** so they flow to every service transitively; each `Program.cs` opts in with one line, `builder.AddObservability("ServiceName")`, plus `.ConfigureOtlpLogging("ServiceName")` on its Serilog config.

- **Traces**: auto-instrumentation for ASP.NET Core, HttpClient, and EF Core, so the gateway → service → service → SQL hops are one connected trace. The exception is the **RabbitMQ hop**: HTTP context propagates automatically, but across the broker the trace context is **injected** into AMQP headers by `RabbitMqPublisher` and **extracted** in `OrderCreatedConsumer` (the standard OTel messaging pattern, done by hand to stay visible) — done via the shared `ActivitySource` in `DiagnosticsConfig`.
- **Metrics**: auto-instrumentation (request rate/latency, HTTP client, .NET runtime) plus a custom business counter `store.orders.created` (`DiagnosticsConfig.OrdersCreated`, incremented in `OrderService.CreateOrderAsync`).
- **Logs**: Serilog is unchanged (console + file) but `ConfigureOtlpLogging` adds an OTLP sink that ships each record to the backend stamped with the active `TraceId`/`SpanId`, so logs correlate with traces.

The OTLP endpoint is `OTEL_EXPORTER_OTLP_ENDPOINT` (defaults to `http://localhost:4317`; Docker sets `http://otel-lgtm:4317` on every service). Swapping backends is an env-var change, not a code change. Full walkthrough: `docs/observability.md`.

### SharedKernel

`src/SharedKernel` is a library (OutputType=Library) referenced by all services. It provides:

- `Auth/JwtExtensions.cs` — `AddJwtBearerValidation()` extension that reads from `JwtSettings` config section
- `Middleware/RateLimitingMiddleware.cs` — Redis sliding-window rate limiter (100 req/min default, IP-keyed)
- `Middleware/RequestLoggingMiddleware.cs` — structured request/response logging via Serilog
- `Middleware/MiddlewareExtensions.cs` — `UseRequestLogging()` / `UseRateLimiting()` convenience extensions
- `Messaging/` — RabbitMQ publisher plumbing (`IEventPublisher`/`RabbitMqPublisher`), topology names (`MessagingTopology`), connection settings (`RabbitMqSettings`)
- `Observability/` — OpenTelemetry wiring: `ObservabilityExtensions.AddObservability()` (traces + metrics) and `ConfigureOtlpLogging()` (Serilog OTLP sink), plus `DiagnosticsConfig` (shared `ActivitySource` + `Meter` + custom counters)
- `DTOs/PaginationDTOs.cs` — shared pagination types

### Caching (ProductCatalogService)

`ProductService` and `CategoryService` are each wrapped by a `Cached*` decorator (`CachedProductService`, `CachedCategoryService`) that uses Redis via `IDistributedCache`. The raw services are registered scoped and the cached wrappers are what gets resolved for `IProductService` / `ICategoryService`.

### JWT config

The gateway reads JWT config from `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`. The downstream services use `JwtSettings:SecretKey`, `JwtSettings:Issuer`, `JwtSettings:Audience` (via `SharedKernel.Auth.JwtExtensions`). These must be kept in sync across all `appsettings.json` files.

### Load balancing (Docker only)

A teaching example demonstrating an L7 load balancer. In `docker-compose.yml`, ProductCatalogService is defined three times via a YAML anchor (`&product-catalog` on `product-catalog-1`, merged into `-2`/`-3`); each replica only overrides its name and debug port. `nginx/catalog-lb.conf` configures the `nginx-catalog-lb` container to fan requests across the three replicas. Everything that previously targeted `product-catalog-service` — the gateway's `/api/products` and `/api/categories` routes, plus OrderService and BffService's `Services__CatalogService` — now points at `nginx-catalog-lb:8080`.

Observability hooks (all unauthenticated, not exposed through the gateway, matching the `internal/` convention): every ProductCatalog response carries an `X-Instance` header stamped with the container hostname (added by middleware in `Program.cs`), and `LbDemoController` (`internal/lb/*`) provides `whoami`, `health`, `health/toggle` (mark an instance unhealthy so Nginx evicts it), and `slow/{ms}` (add latency so `least_conn` diverges from round-robin). The nginx config uses `max_fails`/`fail_timeout` passive health checks and `proxy_next_upstream` for transparent retry/failover.

Run the guided demo with `scripts/lb-demo.sh`; full walkthrough in `docs/load-balancing.md`.
