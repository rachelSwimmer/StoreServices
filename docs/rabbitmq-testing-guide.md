# RabbitMQ — Hands-On Testing Guide

A step-by-step guide to **verify the RabbitMQ messaging flow** in StoreServices.
Use it live in class: each step prints something you can point at.

> Conceptual background (exchange / queue / binding / routing key) is in
> [`rabbitmq-example.md`](./rabbitmq-example.md). This file is just "how to run
> and check it."

**Prerequisites:** Docker running (`docker version` should succeed).

---

## Level A — Test the messaging in isolation (do this first)

This checks **only** the broker + consumer + topology. We don't start SQL Server
or the order saga — we publish a *synthetic* event by hand and watch the consumer
react. If Level A works, the RabbitMQ section is correct; anything that breaks in
Level B is then a saga/database problem, not a messaging one.

### 1. Start just the broker and the consumer

```bash
docker compose up --build -d rabbitmq notification-service
```

### 2. Confirm both are up (RabbitMQ should say `healthy`)

```bash
docker compose ps
```

### 3. Confirm the consumer declared its topology and is listening

```bash
docker compose logs notification-service | grep "Listening for"
```

Expected:

```
notification-service  | [..] Listening for 'order.*' on queue 'notifications.order-created'
```

This proves the consumer connected, declared the `store.events` exchange, the
`notifications.order-created` queue, and bound them with pattern `order.*`.

### 4. Publish a synthetic `order.created` message

We use RabbitMQ's **management HTTP API** to push a message straight into the
exchange — no producer service needed.

> ⚠️ **Keep the whole `-d` payload on ONE line.** If your terminal wraps it and a
> newline lands *inside* the JSON, the body becomes invalid and the API silently
> rejects it — you'll see no `{"routed":true}`. This is the #1 gotcha.

```bash
curl -s -u guest:guest -H "Content-Type: application/json" \
  -X POST http://localhost:15672/api/exchanges/%2f/store.events/publish \
  -d '{"properties":{"content_type":"application/json","delivery_mode":2},"routing_key":"order.created","payload":"{\"OrderId\":99,\"UserId\":1,\"UserName\":\"Test Student\",\"TotalAmount\":42.50,\"OrderDate\":\"2026-05-26T10:00:00Z\"}","payload_encoding":"string"}'; echo
```

Expected response:

```json
{"routed":true}
```

`"routed":true` means the exchange found a matching queue (our binding worked).
If you ever get `"routed":false`, the binding/queue is missing — usually because
the consumer hadn't started yet when you published.

> `%2f` in the URL is the URL-encoded default virtual host `/`. `delivery_mode:2`
> marks the message persistent.

### 5. Confirm the consumer handled it

```bash
docker compose logs notification-service | grep "📧"
```

Expected:

```
notification-service  | [..] 📧 Sending order confirmation to Test Student for order #99 (total $42.50)
```

✅ Seeing `{"routed":true}` **and** the `📧` line proves the full chain works:
**exchange → topic binding → queue → consume → ack.**

### 6. (Optional) See it in the management UI

Open <http://localhost:15672> — login **guest / guest**.

- **Exchanges** tab → `store.events` (type `topic`).
- **Queues** tab → `notifications.order-created`. Re-run step 4 and watch the
  message-rate graph spike, then fall back to 0 as the consumer acks it.

---

## Level B — Full end-to-end (the real producer)

Now the *real* `OrderService` publishes the event itself when an order is placed.
This needs the whole stack (SQL Server, UserAuthService, ProductCatalogService),
because creating an order validates the user and reserves stock first.

### 1. Start everything

```bash
docker compose up --build -d
```

Wait until `docker compose ps` shows the infrastructure healthy.

### 2. Get a JWT

Register or log in through the gateway (port 8082) to obtain a token:

```bash
# example — adjust to your auth endpoint/payload
curl -s -X POST http://localhost:8082/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"someone@example.com","password":"P@ssw0rd!"}'
```

Copy the `token` from the response.

### 3. Place an order (triggers a real `OrderCreated` event)

```bash
curl -s -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer <YOUR_JWT>" \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"shippingAddress":"123 Main St","orderItems":[{"productId":1,"quantity":2}]}'
```

The order returns `201` **immediately** — publishing is fire-and-forget.

### 4. Watch the consumer

```bash
docker compose logs -f notification-service
```

You'll see a `📧 Sending order confirmation …` line for the order you just made.

---

## Bonus demo — decoupling / resilience

Great to show students *why* async messaging matters:

```bash
docker compose stop notification-service     # consumer goes down
# place one or more orders (Level B step 3) — they still return 201!
# in the management UI, notifications.order-created queue depth climbs

docker compose start notification-service    # consumer comes back
# it immediately drains the queued messages and logs each 📧
```

Orders kept working while the consumer was down, and **no message was lost** —
the durable queue held them. That's the payoff of async messaging.

---

## Cleanup

```bash
docker compose down            # stop & remove containers
docker compose down -v         # also delete volumes (fresh DB/broker next time)
```

---

## Quick troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| No `{"routed":true}` from curl | Payload wrapped onto multiple lines → invalid JSON | Put the whole `-d` body on one line |
| `{"routed":false}` | Queue/binding not there yet | Make sure `notification-service` started before publishing (check step 3) |
| curl seems to hang / messy output | Missing `-s` flag | Add `-s` (silent) and a trailing `; echo` |
| No `📧` line | Consumer not running, or wrong routing key | `docker compose ps`; routing key must match `order.*` |
| Every log line appears twice | Serilog writes to both Console and File sinks | Harmless; cosmetic only |
| Can't reach :15672 | Broker still starting | Wait for `healthy` in `docker compose ps` |
