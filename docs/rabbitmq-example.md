# RabbitMQ Example: Event-Driven Order Notifications

A hands-on example of **asynchronous messaging** with RabbitMQ, built into the
StoreServices microservices solution. It is written to be read and explained to
students, using the **raw `RabbitMQ.Client` library** (no MassTransit or other
abstraction) so the underlying AMQP mechanics stay visible.

---

## 1. Why messaging? (vs what the repo already does)

Every other cross-service call in this solution is **synchronous HTTP**. For
example, when `OrderService` creates an order it calls `ProductCatalogService`
over HTTP to reserve stock, and waits (the "stock reservation saga" in
`OrderService.CreateOrderAsync`). Synchronous calls are simple but tightly
coupled: the caller waits, and if the callee is down, the caller fails.

Some work doesn't need to happen *right now, in the request*. Sending an order
confirmation email is a perfect example — the customer shouldn't wait for the
email, and the order shouldn't fail if the email system is briefly down.

That's what this example demonstrates: when an order is placed, `OrderService`
**publishes an event and moves on**. A separate **`NotificationService`**
**consumes** that event whenever it's ready and "sends" the confirmation. The two
services are decoupled in time and in deployment.

---

## 2. The vocabulary (mapped to this code)

```
 PRODUCER                      BROKER (RabbitMQ)                    CONSUMER
 OrderService                                                       NotificationService

  publish  ─ routing key ─►  ┌───────────────┐
            "order.created"  │   exchange     │
                             │ "store.events" │
                             │   (topic)      │
                             └──────┬─────────┘
                                    │ binding pattern "order.*"
                                    ▼
                             ┌────────────────────────────┐
                             │ queue                       │ ──►  consume + ACK
                             │ "notifications.order-created"│      → log "📧 email"
                             └────────────────────────────┘
```

| Term | What it is | Where in the code |
|---|---|---|
| **Producer** | Publishes messages | `OrderService` via `RabbitMqPublisher` |
| **Exchange** | Receives every published message and decides where it goes. We use a **topic** exchange. | `store.events` — `MessagingTopology.ExchangeName` |
| **Routing key** | A label the producer stamps on each message | `order.created` — `MessagingTopology.OrderCreatedRoutingKey` |
| **Queue** | A buffer that holds messages for a consumer | `notifications.order-created` — `MessagingTopology.NotificationsQueue` |
| **Binding** | A rule linking a queue to an exchange by a pattern | `order.*` — `MessagingTopology.NotificationsBindingPattern` |
| **Consumer** | Reads and processes messages | `NotificationService` via `OrderCreatedConsumer` |
| **Ack** | "I've handled this message, you can delete it" | `BasicAck` in `OrderCreatedConsumer` |
| **Prefetch (QoS)** | How many un-acked messages a consumer may hold | `BasicQos(prefetchCount: 1)` |

**Why a *topic* exchange?** A topic exchange routes by matching the routing key
against each binding's pattern, where `*` matches one word and `#` matches many
(words are dot-separated). Our queue binds with `order.*`, so it receives
`order.created` today and would automatically receive `order.cancelled`,
`order.shipped`, etc. if we add them later — without changing existing consumers.

---

## 3. Where the code lives

| File | Role |
|---|---|
| `src/SharedKernel/Messaging/MessagingTopology.cs` | The exchange/queue/routing-key **names** both sides agree on |
| `src/SharedKernel/Messaging/RabbitMqSettings.cs` | Connection config POCO (`RabbitMq` section) |
| `src/SharedKernel/Messaging/IEventPublisher.cs` + `RabbitMqPublisher.cs` | Reusable **publisher plumbing** — generic, knows nothing about orders |
| `src/OrderService/Messaging/OrderCreatedEvent.cs` | **Producer's own** copy of the message contract |
| `src/OrderService/Services/OrderService.cs` | Publishes the event after an order is saved |
| `src/NotificationService/Messaging/OrderCreatedEvent.cs` | **Consumer's own** copy of the message contract |
| `src/NotificationService/Consumers/OrderCreatedConsumer.cs` | The consumer — declares topology, consumes, acks |

---

## 4. The most important design decision: the contract is **duplicated**, not shared

Notice there are **two** `OrderCreatedEvent` classes — one in `OrderService`,
one in `NotificationService` — and they are deliberately **not** a single shared
type in `SharedKernel`.

**Why?** The whole promise of microservices is **independent deployability**. If
both services referenced one shared `OrderCreatedEvent` class:

- Changing that class forces **both** services to rebuild and redeploy together.
- You've quietly recreated a **distributed monolith** — the coupling messaging
  was supposed to remove.

Instead, the services agree only on the **JSON shape on the wire** (the field
names). This is the **"tolerant reader"** pattern:

- `OrderService` can add a new field to its event and deploy on its own.
- `NotificationService`, running its old copy, simply **ignores** unknown JSON
  fields and keeps working. (Look at the consumer's copy — it doesn't even
  declare the `Items` field, because notifications don't need it.)

> **The lesson:** services share *contracts* (the message format), not *code*
> (a compiled type). The network is the boundary.
>
> What *is* fine to share is genuinely reusable **technical plumbing** — the
> RabbitMQ connection/publish helper (`RabbitMqPublisher`) and the topology
> *names* — which is why those live in `SharedKernel` while the business event
> does not.

---

## 5. Run it

```bash
docker compose up --build
```

Wait until `rabbitmq` is healthy and `order-service` / `notification-service`
have started (watch the logs — the consumer prints
`Listening for 'order.*' on queue 'notifications.order-created'`).

### Trigger an event

Place an order through the gateway (port 8082). You'll need a JWT first:

```bash
# 1. Register / log in via UserAuthService to get a token, then:
curl -X POST http://localhost:8082/api/orders \
  -H "Authorization: Bearer <YOUR_JWT>" \
  -H "Content-Type: application/json" \
  -d '{
        "userId": 1,
        "shippingAddress": "123 Main St",
        "orderItems": [ { "productId": 1, "quantity": 2 } ]
      }'
```

The order is created and returns `201` **immediately** — publishing the event is
fire-and-forget.

### See the message flow

- **Container logs:**
  ```bash
  docker compose logs -f notification-service
  ```
  You'll see: `📧 Sending order confirmation to <name> for order #<id> (total $<amount>)`

- **Management UI:** open <http://localhost:15672> (login **guest / guest**).
  - **Exchanges** tab → `store.events` (type `topic`).
  - **Queues** tab → `notifications.order-created` — watch the message rate spike
    when you place an order, then drop back to zero as the consumer acks it.

---

## 6. Demo: decoupling in action

This is the payoff. Stop the consumer, place orders, then bring it back:

```bash
docker compose stop notification-service

# place one or more orders — they still succeed (201)!
# In the management UI, the notifications.order-created queue depth climbs.

docker compose start notification-service
# watch it immediately drain the queued messages and log each "📧 ..."
```

Orders kept working while the consumer was down, and **no message was lost** —
the durable queue held them until the consumer returned. That's async messaging.

---

## 7. What this example deliberately simplifies (discussion points)

Good things to raise with students once the basics click:

- **At-least-once delivery & idempotency.** Because we ack *after* processing, a
  crash mid-handling causes RabbitMQ to **redeliver**. Real consumers must be
  **idempotent** (e.g., dedupe by `OrderId`) so a duplicate "email" isn't sent.
- **The dual-write / transactional outbox problem.** We save the order to SQL
  **and then** publish to RabbitMQ in two separate steps. If the process dies in
  between, the order exists but no event was published — the event is lost. The
  production fix is the **transactional outbox** pattern: write the event into
  the same DB transaction as the order, then a separate relay publishes it.
- **Dead-letter queues.** Our consumer `BasicNack`s a poison message with
  `requeue: false` so it doesn't loop forever; in production you'd route it to a
  **dead-letter queue** for inspection instead of dropping it.
- **Raw client vs a library.** We used `RabbitMQ.Client` directly to expose the
  mechanics. A real project often uses **MassTransit** to remove this
  boilerplate (consumers become simple classes, retries/outbox/DLQ are built in).
