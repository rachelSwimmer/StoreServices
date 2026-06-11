# Observability: Traces, Metrics & Logs with OpenTelemetry

A hands-on tour of the **three pillars of observability** wired into the
StoreServices solution with **OpenTelemetry (OTel)**. Like the RabbitMQ example,
it's written to be read and explained to students: the plumbing lives in
`SharedKernel` and the interesting mechanics (trace context across the message
broker) are done by hand so they stay visible.

---

## 1. Why observability? (and why one standard)

With seven services, one Nginx load balancer and a message broker, a single user
action ("place an order") fans out across many processes. When it's slow or
broken, "which log file?" is the wrong question. You need to follow *one request*
across *every* hop.

The three pillars answer three different questions:

| Pillar | Answers | Example here |
|---|---|---|
| **Traces** | *Where* did the time go / where did it break? | gateway → OrderService → CatalogService → RabbitMQ → NotificationService, as one timeline |
| **Metrics** | *How much / how often?* (aggregate health) | requests/sec, p95 latency, error %, orders created |
| **Logs** | *What exactly happened* in this one case? | "Order 42 created", "stock reservation failed" |

The magic is **correlation**: one request gets a `trace_id` that rides along on
every HTTP hop *and* through the broker, and every log line it produces carries
that same id. In the UI you click a slow span and jump straight to its logs.

**OpenTelemetry** is the vendor-neutral standard for emitting all three. You
instrument once; the data exports over one wire protocol (**OTLP**) to whatever
backend you like. Swapping backends is an environment-variable change, never a
code change — that's the whole point.

---

## 2. The backend: `grafana/otel-lgtm`

One container (`docker-compose.yml`) bundles the whole receiving stack:

```
                          ┌──────────────── otel-lgtm ────────────────┐
   our services  ──OTLP──►│  OTel Collector → Tempo     (traces)      │
   (port 4317, gRPC)      │                 → Prometheus (metrics)    │
                          │                 → Loki       (logs)       │──► Grafana
                          └───────────────────────────────────────────┘     :3000
```

- **Tempo** stores traces, **Prometheus** stores metrics, **Loki** stores logs —
  three different stores, because the three pillars have different shapes.
- **Grafana** (http://localhost:3000) is the single UI over all three.

These are production-grade tools; bundling them just removes the wiring so the
lesson stays on the concepts.

---

## 3. How a service is instrumented (one line each)

All the reusable wiring is in `SharedKernel/Observability/`, mirroring how JWT
and messaging plumbing already live in SharedKernel. A service opts in with:

```csharp
builder.AddObservability("OrderService");        // traces + metrics
// ...and on the Serilog config:
    .ConfigureOtlpLogging("OrderService")        // logs → same backend
```

`AddObservability` (`ObservabilityExtensions.cs`) registers:

- **Tracing**: auto-instrumentation for incoming **ASP.NET Core** requests,
  outgoing **HttpClient** calls, and **EF Core** SQL — plus our own
  `ActivitySource` for the RabbitMQ hop (next section).
- **Metrics**: ASP.NET Core + HttpClient + .NET runtime counters, plus our custom
  business `Meter`.
- An **OTLP exporter** pointed at `OTEL_EXPORTER_OTLP_ENDPOINT`
  (defaults to `http://localhost:4317`; Docker sets it to `http://otel-lgtm:4317`).

Logs stay on **Serilog** — console and file sinks are untouched — but
`ConfigureOtlpLogging` adds an OTLP sink that also ships each log record to the
backend, automatically stamped with the active `TraceId`/`SpanId`. That stamp is
what links a log line back to its trace.

---

## 4. The interesting bit: keeping the trace alive across RabbitMQ

Across **HTTP**, trace context propagates for free: the SDK writes a
`traceparent` header on the outgoing request and reads it on the way in. So
gateway → service → service is automatically one connected trace.

A **message broker breaks that chain** — there's no HTTP request to carry the
header. If you do nothing, NotificationService starts a brand-new, disconnected
trace and the order flow looks like two unrelated things. So we carry the context
by hand (this is the standard OTel messaging pattern):

**Publish side** — `SharedKernel/Messaging/RabbitMqPublisher.cs`:

```csharp
using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
    $"{routingKey} publish", ActivityKind.Producer);
// ...
Propagator.Inject(
    new PropagationContext(contextToInject, Baggage.Current),
    props.Headers,                                   // write into AMQP headers
    static (headers, key, value) => headers[key] = value);
```

**Consume side** — `NotificationService/Consumers/OrderCreatedConsumer.cs`:

```csharp
var parentContext = Propagator.Extract(default, ea.BasicProperties, ExtractHeader);
using var activity = DiagnosticsConfig.ActivitySource.StartActivity(
    $"{ea.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext);
```

The custom `ActivitySource` (`SharedKernel/Observability/DiagnosticsConfig.cs`) is
registered with the tracer via `.AddSource(...)` in `AddObservability`, so these
spans are exported like any other. Result: the producer span and the consumer
span share the order's `trace_id`, and the broker hop shows up *inside* the same
trace.

---

## 5. Custom metrics (domain signals)

Auto-instrumentation gives technical metrics (request rate, latency). Business
questions need business metrics. `DiagnosticsConfig` defines a counter:

```csharp
public static readonly Counter<long> OrdersCreated =
    Meter.CreateCounter<long>("store.orders.created", ...);
```

and `OrderService.CreateOrderAsync` increments it once per persisted order:

```csharp
DiagnosticsConfig.OrdersCreated.Add(1);
```

The `Meter` is registered with `.AddMeter(...)` in `AddObservability`, so the
counter flows to Prometheus and is queryable as `store_orders_created_total`.

---

## 6. Run the demo

> Presenting this to a class? See **[observability-demo-guide.md](observability-demo-guide.md)**
> for a step-by-step presenter's runbook: the exact curl flow, what to open in
> Grafana for each pillar, the queries to paste, and the common "looks broken"
> gotchas (the demo user must be registered first, dots-vs-underscores in metric
> names, time-range, etc.).

```bash
docker compose up --build
```

Wait for the stack to come up, then generate some traffic. The full
cross-service trace (and the `store.orders.created` metric) only appears when an
order is placed, which requires a JWT — register/login via `/api/auth/*` and
`POST` an order through the gateway (see the main README / rabbitmq-example.md
for the request flow). For a quick no-auth hit that still produces traces and
HTTP metrics, call the catalog load balancer directly:

```bash
curl http://localhost:8085/api/products
```

(`http://localhost:8082/api/products` through the gateway returns **401** without
a token — that's expected.)

Open **Grafana → http://localhost:3000** (no login needed) and use **Explore**:

1. **Traces** — pick the **Tempo** datasource → *Search* → run. Open a trace for a
   placed order. You should see one timeline spanning **api-gateway →
   order-service**, the **EF Core SQL** spans, the HTTP calls to
   **user-auth/catalog**, the **`order.created publish`** span, and the
   **`order.created receive`** span in **notification-service** — all in one trace.
2. **Logs** — on a span, use **Logs for this span** (or switch to the **Loki**
   datasource and filter `{service_name="OrderService"}` — the value is the
   PascalCase name passed to `AddObservability`, not the container name, and Loki
   requires at least one `{...}` label matcher). The log lines carry the same
   `trace_id`.
3. **Metrics** — pick the **Prometheus** datasource (Explore → datasource
   dropdown), switch the editor to **Code** mode, paste a query from the cookbook
   below, and **Run query**. See `store_orders_created_total` for our custom
   counter.

### PromQL cookbook

Paste one query at a time (Prometheus datasource, **Code** mode, time range *Last
1 hour*). Counters are cumulative — `rate()`/`increase()` turn them into "per
second" / "in the last N minutes". `sum by (...)` collapses the per-replica /
per-route series into the grouping you care about.

```promql
# Custom business metric: total orders created (cumulative odometer)
store_orders_created_total

# Orders created in the last 5 minutes (handles counter resets on restart)
sum(increase(store_orders_created_total[5m]))

# Incoming request rate (req/sec) per service and route
sum by (service_name, http_route) (rate(http_server_request_duration_seconds_count[1m]))

# p95 incoming latency per service — the classic SLO graph
histogram_quantile(0.95, sum by (le, service_name) (rate(http_server_request_duration_seconds_bucket[5m])))

# Error rate: 5xx responses per second per service
sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))

# Outgoing HttpClient p95 latency — the service-to-service hops (gateway, BFF, order)
histogram_quantile(0.95, sum by (le, service_name) (rate(http_client_request_duration_seconds_bucket[5m])))

# .NET runtime: managed heap size per service
process_runtime_dotnet_gc_heap_size_bytes

# .NET runtime: exceptions thrown per second
rate(process_runtime_dotnet_exceptions_count_total[5m])
```

> Rates/histograms look empty without recent traffic — generate some load first,
> then re-run. Switch the result panel from **Table** to **Time series** for a graph.

---

## 7. What's deliberately left as an exercise

- **Redis cache spans** — `OpenTelemetry.Instrumentation.StackExchangeRedis` would
  add a span per cache hit/miss in ProductCatalogService and BffService. It needs
  the `IConnectionMultiplexer`, so it's wired per-service rather than in the
  generic helper.
- **A cache hit-ratio metric** — the `Cached*` decorators already log hits/misses;
  adding a `Counter`/`Histogram` there is a natural follow-up.
- **Sampling** — everything is exported (sampling = always-on), which is fine for
  a demo. Production uses tail or probabilistic sampling to control volume.

---

## 8. File map

| Concern | File |
|---|---|
| ActivitySource + Meter + counters | `src/SharedKernel/Observability/DiagnosticsConfig.cs` |
| `AddObservability` + Serilog OTLP helper | `src/SharedKernel/Observability/ObservabilityExtensions.cs` |
| Trace context **inject** (publish) | `src/SharedKernel/Messaging/RabbitMqPublisher.cs` |
| Trace context **extract** (consume) | `src/NotificationService/Consumers/OrderCreatedConsumer.cs` |
| Custom metric increment | `src/OrderService/Services/OrderService.cs` |
| Per-service opt-in | each `src/*/Program.cs` |
| Backend + OTLP endpoint env | `docker-compose.yml` (`otel-lgtm`) |
