# Observability Demo Guide (for presenting to students)

A **presenter's runbook**: exactly what to type, what to open in Grafana, and what
to point at for each of the three pillars. The *concepts* live in
[`observability.md`](observability.md); this file is the live-demo script.


---

## 0. Before the class: pre-flight (do this once, off-screen)

```bash
# 1. Everything up, including the otel-lgtm backend
docker compose up --build -d

# 2. otel-lgtm must be healthy before any telemetry can land
docker compose ps otel-lgtm        # STATUS should say (healthy)

# 3. Grafana is reachable, no login
#    http://localhost:3000  →  Explore (compass icon, left rail)
```

**The #1 gotcha that makes it look "broken":** the demo login user does **not**
exist until you create it. Register it first, or your `curl` login returns
`"Invalid email or password."`, `$TOKEN` becomes `null`, the order POST 401s, and
**nothing is created** — so there's nothing to show.

```bash
# Create the demo user (phone is required and must be a valid number)
curl -s -X POST http://localhost:8082/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"YourPassword123",
       "firstName":"You","lastName":"Test","phone":"+15551234567"}'
# Expect: HTTP 201 and a JSON user with an "id"
```

---

## 1. The live action: place one order

Run this on-screen. The `-w` line prints the HTTP status so a failure is visible
instead of silent.

```bash
# Log in -> capture JWT. If this fails, STOP: everything downstream is 401.
TOKEN=$(curl -s -X POST http://localhost:8082/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"ttt@example.com","password":"Aa123123!"}' | jq -r .token)
echo "token starts with: ${TOKEN:0:20}..."   # should NOT be 'null'

# Place the order. This is the one click that fans out across every service.
curl -s -w "\nHTTP %{http_code}\n" -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"shippingAddress":"123 Main St",
       "orderItems":[{"productId":1,"quantity":2}]}'
# Expect: HTTP 201 and an order JSON with an "id"
```

Say: *"That one request just travelled gateway → OrderService → CatalogService
(reserve stock) → SQL → RabbitMQ → NotificationService. Let's watch it."*

> Tip: run it 5–10 times in a loop so the metric graphs have a shape to show.
> ```bash
> for i in $(seq 10); do curl -s -o /dev/null -X POST http://localhost:8082/api/orders \
>   -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
>   -d '{"userId":1,"shippingAddress":"123 Main St","orderItems":[{"productId":1,"quantity":2}]}'; done
> ```

---

## 2. TRACES — *"where did the time go?"* (Tempo)

**Grafana → Explore → datasource dropdown → `Tempo`.**

1. **Query type: `Search`.** Set **Service Name = `OrderService`**, click **Run query**.
2. Pick the most recent trace → the **waterfall** opens on the right.

**What to point at:**
- The **spans nest**: `POST /api/orders` (gateway) → OrderService → an
  **HttpClient** span to CatalogService → an **EF Core** span for the SQL insert.
  *"Each bar is a hop; the width is how long it took."*
- The **RabbitMQ publish span** (`order.created`) — emphasise this one: HTTP
  context propagates for free, but **across the broker we inject/extract the trace
  id by hand** so NotificationService's work joins the *same* trace.
- Copy the **Trace ID** from the top — you'll paste it into Loki in step 4 to show
  correlation.

**Power move (TraceQL):** switch query type to **TraceQL** and paste:
```
{ name = "POST api/orders" }            // find the order requests
{ duration > 500ms }                    // find slow requests, any service
{ span.db.system = "mssql" }            // find traces that hit SQL
```

**If empty:** the time range (top-right) doesn't cover when you sent the request,
or you searched the wrong service. Set range to *Last 15 minutes* and re-run.

---

## 3. METRICS — *"how much / how often?"* (Prometheus)

**Grafana → Explore → datasource dropdown → `Prometheus`.** Switch the editor to
**Code** mode, paste one query, **Run query**.

| What to show | Query | What to say |
|---|---|---|
| **Our custom business metric** | `store_orders_created_total` | *"We wrote this counter ourselves — `DiagnosticsConfig.OrdersCreated`, bumped in `CreateOrderAsync`."* |
| Orders in last 5 min | `sum(increase(store_orders_created_total[5m]))` | survives counter resets on restart |
| Request rate per route | `sum by (service_name, http_route) (rate(http_server_request_duration_seconds_count[1m]))` | auto-instrumentation, *we wrote zero code* |
| p95 latency (the SLO graph) | `histogram_quantile(0.95, sum by (le, service_name) (rate(http_server_request_duration_seconds_bucket[5m])))` | the classic dashboard line |
| Error rate (5xx/sec) | `sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))` | trigger it by sending a bad order |

**The naming gotcha to teach explicitly:** the code says
`store.orders.created` but Prometheus shows `store_orders_created_total` — dots
become underscores and counters get a `_total` suffix. *"If you search for the
dotted name you'll think nothing's there. It's an OTel→Prometheus convention."*

---

## 4. LOGS — *"what exactly happened?"* and the payoff: **correlation** (Loki)

**Grafana → Explore → datasource dropdown → `Loki`.**

1. **Code** mode, query: `{service_name="OrderService"}`, **Run query**.
   - Point at the lines from your order: `Published message to exchange
     'store.events' with routing key 'order.created'` and
     `POST /api/orders responded 201`.
   - **Naming gotcha (again):** the label is `service_name`, not `service.name`.

2. **The money shot — one request across everything.** Take the **Trace ID** you
   copied from Tempo in step 2 and query:
   ```
   {service_name=~".+"} | trace_id = "<paste-trace-id-here>"
   ```
   *"Every log line from every service for this one order — because Serilog stamps
   each record with the active trace id."*

3. **Click → jump.** Expand any log line: the `trace_id` field has a **Tempo**
   link. Click it and Grafana opens the trace from step 2. *"This is the whole
   point of observability — logs, metrics and traces are one connected story, not
   three separate tools."*

---

## 5. Suggested 10-minute running order

1. Place the order live (step 1) — 1 min.
2. **Trace** first: it's the most visual, sets the mental model (step 2) — 3 min.
3. **Metrics**: zoom out to aggregate health; show the custom counter (step 3) — 3 min.
4. **Logs + correlation**: paste the trace id, click the link back to Tempo (step 4) — 3 min.

End on the trace→log→trace round-trip — that's the "aha".

---

## 6. Fast troubleshooting (if a pillar looks empty)

| Symptom | Cause | Fix |
|---|---|---|
| Login returns `Invalid email or password` | demo user never created | run the register call in §0 |
| Order returns `401` | `$TOKEN` is `null` from a failed login | fix login first; check `echo $TOKEN` |
| All three pillars empty | wrong **time range** in Explore | set top-right to *Last 15 minutes* |
| Metric "missing" | searched `store.orders.created` | use `store_orders_created_total` (underscores, `_total`) |
| Logs "missing" | searched `service.name` | label is `service_name` |
| Genuinely no data anywhere | otel-lgtm not healthy / wrong endpoint | `docker compose ps otel-lgtm`; services need `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-lgtm:4317` |

Quick CLI sanity checks (no UI needed), proving data reached each store:

```bash
PROM=http://localhost:3000/api/datasources/proxy/uid/prometheus
curl -s "$PROM/api/v1/label/job/values" | jq          # lists every reporting service
curl -s "$PROM/api/v1/query?query=store_orders_created_total" | jq '.data.result'
```

---

## 7. The payoff demo: "something broke — find it in 60 seconds"

This is the part that sells observability. **Break something on purpose, then race
to diagnose it using the three pillars** instead of `docker logs`-ing seven
containers by hand.

The teaching arc is always the same loop:
**Metric tells you *that* it's broken → Trace tells you *where* → Log tells you *why*.**

Run two scenarios back-to-back so students see the crucial difference between a
**logical failure** (the system worked correctly, the user just can't have what
they asked for) and an **infrastructure failure** (a real outage).

> Get a token first (§1) and keep it in `$TOKEN`.

### Scenario A — logical failure: out of stock (expected, *not* a bug)

```bash
curl -s -w "\nHTTP %{http_code}\n" -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"userId":1,"shippingAddress":"x","orderItems":[{"productId":1,"quantity":999999}]}'
# -> HTTP 409  {"message":"Insufficient stock for 'product 1'. Available: 0"}
```

Walk the pillars:
- **Trace** (Tempo, Search `OrderService`): the trace **completes** — you see the
  HttpClient span to the catalog's `reserve-stock`, it returns, and the order is
  rejected cleanly. *No red spans.* Point out: *"This isn't an error — the saga
  did its job. The stock-reservation step said no."*
- **Metric**: shows up as a **4xx** (409), not a 5xx. Error-budget dashboards
  usually ignore 4xx for exactly this reason — *"client asked for the impossible"*
  is not an outage.
- **Log**: a single informative line on OrderService. No stack trace.

Lesson: **a 409 is the system being healthy.** Don't page someone for it.

### Scenario B — infrastructure failure: a downstream service is down

Kill the catalog load balancer so OrderService's `reserve-stock` call has nowhere
to go, then place a **normal** order:

```bash
docker compose stop nginx-catalog-lb           # simulate the outage

curl -s -w "\nHTTP %{http_code}\n" -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"userId":1,"shippingAddress":"x","orderItems":[{"productId":1,"quantity":1}]}'
# -> HTTP 500  {"detail":"Resource temporarily unavailable (nginx-catalog-lb:8080)", ...}
```

Now run the diagnosis loop live — *time yourself, it's the dramatic bit*:

1. **METRIC — "is something wrong?"** (Prometheus, Code mode):
   ```promql
   sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[1m]))
   ```
   The line for **OrderService** lifts off zero. *"We didn't read a log. The graph
   told us OrderService is throwing 500s right now."*

2. **TRACE — "where is it breaking?"** (Tempo). Switch to **TraceQL** and ask only
   for failed traces:
   ```
   { status = error }
   ```
   Open one → the waterfall has a **red HttpClient span** pointing at
   `nginx-catalog-lb:8080`. *"The break isn't in OrderService's own code — it's the
   call out to the catalog. We've localised the fault to one hop without reading a
   single line of code."* Copy the **Trace ID**.

3. **LOG — "why exactly?"** (Loki). Paste the trace id to pull every log line for
   *this one failed request* across all services:
   ```
   {service_name=~".+"} | trace_id = "<paste-trace-id>"
   ```
   The OrderService line carries the exception: *connection refused /
   "Resource temporarily unavailable (nginx-catalog-lb:8080)"*. **Root cause, in
   three clicks.**

**Restore the system** (don't leave it broken for the next demo):

```bash
docker compose start nginx-catalog-lb
sleep 2
# confirm recovery: a normal order should be 201 again
curl -s -o /dev/null -w "recovered: HTTP %{http_code}\n" -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"userId":1,"shippingAddress":"x","orderItems":[{"productId":1,"quantity":1}]}'
```

Bonus: re-run the 5xx metric query and show the line **fall back to zero** — that
recovery curve is what an on-call engineer watches after a fix.

### The one-slide summary to leave on screen

| Pillar | Question it answers | In scenario B you saw |
|---|---|---|
| **Metric** | *Is something wrong, and how bad?* | OrderService 5xx rate jumped |
| **Trace** | *Where in the request did it break?* | red span → `nginx-catalog-lb` hop |
| **Log** | *Why, exactly, in this one case?* | "connection refused" on that trace id |

*"Without this, scenario B is: SSH into seven containers and grep. With it: three
clicks. That's the entire pitch."*

### Other failures you can stage the same way

- **Stop `user-auth-service`** instead — OrderService also calls it for the
  user-summary; the trace's red span moves to the *auth* hop. Good for showing the
  trace pinpoints *which* dependency, not just "a dependency".
- **Stop `rabbitmq`**: the order still returns **201** (publishing is
  fire-and-forget) but **NotificationService never logs a confirmation**. This
  teaches *silent* failures — metrics/traces look fine, the gap only shows as a
  *missing* log downstream. A great "why we also alert on absence" discussion.
- **Slow, not dead** (`internal/lb/slow/{ms}` only delays the LB's own `whoami`,
  so for a latency story prefer pausing a container: `docker compose pause
  user-auth-service` for a few seconds) — watch p95 latency climb on the
  Prometheus `histogram_quantile(...)` query, then unpause.
