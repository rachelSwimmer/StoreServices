# Load Balancing — A Hands-On Lesson

A self-contained walkthrough of what a load balancer does, using the
StoreServices stack. By the end you will have *seen*, not just heard,
the four core abilities of a load balancer.

> **Prerequisite:** the stack is running.
> ```bash
> docker compose up --build -d
> ```
> All demo steps are driven by `scripts/lb-demo.sh`.

---

## 0. The mental model

A single service instance is a single point of failure. If that one box is
busy or dead, every client is stuck. The fix: run several identical copies
and put a **load balancer (LB)** in front of them. Clients only ever talk to
the LB — they never know, or care, how many copies exist behind it.

```
client ──▶ API Gateway ──▶ [ Nginx LB ] ──┬─▶ catalog-1
                                           ├─▶ catalog-2
                                           └─▶ catalog-3
                                           (shared SQL + Redis)
```

In this project the three copies are `product-catalog-1/2/3`, and the LB is
the `nginx-catalog-lb` container. The real API Gateway sends all
`/api/products` and `/api/categories` traffic through it.

### The first problem to solve

*How do you even prove the LB is doing anything?* Every response looks the
same. The answer: **each copy signs its work.** Every ProductCatalog
instance stamps its container hostname onto:

- an `X-Instance` response header (on *all* responses), and
- a dedicated endpoint: `GET /internal/lb/whoami` → `{"instance":"<host>"}`.

Without identifiable backends, load balancing is invisible. Everything below
depends on this one idea.

---

## 1. The backends are real and distinct

```bash
curl -s http://localhost:8085/internal/lb/whoami
curl -s http://localhost:8085/internal/lb/whoami
curl -s http://localhost:8085/internal/lb/whoami
```

```
{"instance":"5d895331dd0d"}
{"instance":"86069f8bb10f"}
{"instance":"947311fd95b7"}
```

Same URL, same LB, a different worker answering each call. That rotation
*is* load balancing.

---

## 2. Ability 1 — Distribution

> **Goal:** spread load so no single instance is overwhelmed.

```bash
bash scripts/lb-demo.sh distribution
```

```
Distribution over 30 requests:
  5d895331dd0d           10
  86069f8bb10f           10
  947311fd95b7           10
Non-200 responses: 0
```

This is **round-robin**: requests go 1 → 2 → 3 → 1 → 2 → 3 … An even split,
every instance pulling its weight.

*Discussion:* round-robin treats all instances as equal. What if one is
twice as powerful, or twice as slow? Hold that thought — see step 5.

---

## 3. Ability 2 — Failover

> **Goal:** survive an instance dying, with no client-visible errors.

```bash
bash scripts/lb-demo.sh failover
```

```
Stopping product-catalog-2 ...
Distribution over 30 requests:
  86069f8bb10f           15
  947311fd95b7           15
Non-200 responses: 0          ◀── the key line
```

Two things to notice:

1. **`Non-200 responses: 0`** — we killed a server mid-traffic and *not one
   client saw an error.* Nginx detected the dead instance and transparently
   retried on a healthy one (`proxy_next_upstream`).
2. Only the two survivors appear in the tally.

After the script restarts the instance, it automatically rejoins the
rotation. **This is why a load balancer is an availability tool, not just a
speed tool.**

---

## 4. Ability 3 — Health-check eviction

> **Goal:** stop sending traffic to an instance that is *alive but sick*.

This is the subtle one. Contrast with step 3:

- **Failover** = the instance is *gone* (crashed / stopped).
- **Eviction** = the instance is *up and accepting connections* but
  returning errors.

```bash
bash scripts/lb-demo.sh eviction
```

```
Marking product-catalog-2 unhealthy (it stays running) ...
Distribution over 30 requests:
  86069f8bb10f           15
  947311fd95b7           15
Non-200 responses: 0

still running: product-catalog-2 (Up 4 minutes)   ◀── still UP …
(expected: instance-2 absent from rotation though its container is up)
```

The toggled instance now returns HTTP 503. A naive LB would keep sending it
traffic because "the container is still up." Nginx instead watches the
*responses*: after `max_fails=2` failures it benches the instance for
`fail_timeout=10s`, then probes it again. When we restore health, it returns
to an even `10/10/10` split.

---

## 5. Ability 4 — Algorithm choice

> **Goal:** understand that "load balancing" is not a single behaviour.

Honesty first: with three identical, instant servers, **round-robin and
least-connections are mathematically identical.** To see a difference you
need uneven latency *and* concurrent load — so the demo deliberately makes
one instance slow (400 ms) and fires concurrent requests.

```bash
bash scripts/lb-demo.sh algorithm
```

```
Making instance c520d2291e54 artificially slow (400ms) ...

[round-robin] — assigns evenly regardless of latency:
  1ca398b3d616           20
  ae1282a4c911           20
  c520d2291e54           20      ◀── slow node still gets a full third

[least_conn] — steers away from the slow node (c520d2291e54 gets fewer):
  1ca398b3d616           33
  ae1282a4c911           25
  c520d2291e54            2      ◀── slow node starved automatically
```

- **round-robin** is *latency-blind*: it blindly takes turns, so the slow
  instance still gets a third of the traffic and builds a queue.
- **least_conn** sends each new request to the instance with the fewest
  in-flight requests. Requests pile up on the slow box, so it naturally
  receives almost none — the LB routed around the slow server **without
  anyone telling it which one was slow.**

Takeaway: algorithm choice is a real engineering decision. Round-robin is
simple and fair when instances are equal; least-connections adapts when they
are not.

---

## 6. It is wired into the real architecture

This is not a toy bolted on the side. The API Gateway routes through the LB:

```bash
grep nginx-catalog-lb src/ApiGateway/ocelot.Production.json
```

And every real product response proves which instance served it:

```bash
curl -s -D - -o /dev/null http://localhost:8085/internal/lb/whoami | grep -i x-instance
```

---

## Summary

| Ability | Question it answers | What you saw |
|---|---|---|
| **Distribution** | "Don't overload one box" | even 10/10/10 split |
| **Failover** | "What if a box dies?" | 0 client errors; survivors absorb it |
| **Health eviction** | "What if a box is sick but alive?" | bad node benched while still `Up` |
| **Algorithm** | "Are all boxes equal?" | round-robin 20/20/20 vs least_conn 33/25/2 |

> **One sentence to remember:** a load balancer turns N unreliable servers
> into one reliable-looking service.

---

## How it is built (reference)

| File | Role |
|---|---|
| `docker-compose.yml` | 3 `product-catalog-*` replicas + `nginx-catalog-lb` |
| `nginx/catalog-lb.conf` | upstream, `max_fails`/`fail_timeout`, `proxy_next_upstream`, `least_conn` toggle |
| `src/ProductCatalogService/Controllers/LbDemoController.cs` | `whoami` / `health` / `health/toggle` / `slow/{ms}` |
| `src/ProductCatalogService/Program.cs` | middleware stamping the `X-Instance` header |
| `src/ApiGateway/ocelot.Production.json` | gateway routes pointed at the LB |
| `scripts/lb-demo.sh` | the demo driver (`distribution\|failover\|eviction\|algorithm\|all`) |

Demo ports: LB `8085`, replicas `8091/8092/8093` (direct, for targeting one
instance's health/latency toggle).

Tear down with `docker compose down`.
