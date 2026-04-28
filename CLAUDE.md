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

This is a .NET 8 microservices solution with four services and a shared library:

| Project | Port (local) | Port (Docker) | Database |
|---|---|---|---|
| ApiGateway | 5000 | 8082 | — |
| UserAuthService | 5019 | 8080 | UserAuthDb |
| ProductCatalogService | 5149 | 8081 | CatalogDb |
| OrderService | 5150 | 8083 | OrderDb |

Infrastructure: SQL Server 2022 (port 1434 locally / 1433 in Docker), Redis (port 6380 locally / 6379 in Docker).

### Request flow

All external traffic enters through **ApiGateway**, which uses **Ocelot** as a reverse proxy. Routes are defined in `src/ApiGateway/appsettings.json`. The gateway validates JWT tokens before forwarding most routes; `/api/auth/*` is unauthenticated. Redis-backed rate limiting middleware in SharedKernel is applied at both the gateway and service level (skipped gracefully when Redis is unavailable).

### Service-to-service communication

**OrderService** calls the other two services directly over HTTP — not through the gateway. These calls go to `internal/` endpoints that are **not JWT-protected** and not exposed through the gateway:

- `GET internal/users/{id}/exists` and `GET internal/users/{id}/summary` on UserAuthService
- `GET internal/products/{id}/price-and-stock`, `POST internal/products/reserve-stock`, `POST internal/products/release-reservation` on ProductCatalogService

The target URLs are configured in `appsettings.json` under `Services:UserAuthService` and `Services:CatalogService`.

### Stock reservation saga

When `OrderService` creates an order it calls `ReserveStock` on each item. If anything fails mid-loop it releases all already-acquired reservations (`ReleaseReservation`) as a compensation step. Reservations have a 5-minute TTL and a `ReservationSweeperService` background worker in ProductCatalogService sweeps expired ones every 60 seconds.

### SharedKernel

`src/SharedKernel` is a library (OutputType=Library) referenced by all four services. It provides:

- `Auth/JwtExtensions.cs` — `AddJwtBearerValidation()` extension that reads from `JwtSettings` config section
- `Middleware/RateLimitingMiddleware.cs` — Redis sliding-window rate limiter (100 req/min default, IP-keyed)
- `Middleware/RequestLoggingMiddleware.cs` — structured request/response logging via Serilog
- `Middleware/MiddlewareExtensions.cs` — `UseRequestLogging()` / `UseRateLimiting()` convenience extensions
- `DTOs/PaginationDTOs.cs` — shared pagination types

### Caching (ProductCatalogService)

`ProductService` and `CategoryService` are each wrapped by a `Cached*` decorator (`CachedProductService`, `CachedCategoryService`) that uses Redis via `IDistributedCache`. The raw services are registered scoped and the cached wrappers are what gets resolved for `IProductService` / `ICategoryService`.

### JWT config

The gateway reads JWT config from `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`. The downstream services use `JwtSettings:SecretKey`, `JwtSettings:Issuer`, `JwtSettings:Audience` (via `SharedKernel.Auth.JwtExtensions`). These must be kept in sync across all `appsettings.json` files.
