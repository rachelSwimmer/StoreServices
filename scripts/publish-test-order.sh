#!/usr/bin/env bash
#
# Publishes a synthetic "order.created" event straight to the RabbitMQ
# "store.events" exchange via the management HTTP API — no producer service
# needed. Use it to test the NotificationService consumer in isolation.
#
# Usage:
#   ./scripts/publish-test-order.sh                 # uses defaults below
#   ./scripts/publish-test-order.sh 123 "Alice" 55  # OrderId, UserName, Total
#
# Requires: rabbitmq container running (docker compose up -d rabbitmq notification-service)

set -euo pipefail

ORDER_ID="${1:-99}"
USER_NAME="${2:-Test Student}"
TOTAL="${3:-42.50}"

# Build the inner business message (this is the JSON the consumer deserializes).
PAYLOAD=$(cat <<JSON
{"OrderId":${ORDER_ID},"UserId":1,"UserName":"${USER_NAME}","TotalAmount":${TOTAL},"OrderDate":"$(date -u +%Y-%m-%dT%H:%M:%SZ)"}
JSON
)

# Wrap it in the management-API envelope. jq would be cleaner, but we keep it
# dependency-free; python3 safely escapes the payload string into JSON.
BODY=$(python3 - "$PAYLOAD" <<'PY'
import json, sys
payload = sys.argv[1]
print(json.dumps({
    "properties": {"content_type": "application/json", "delivery_mode": 2},
    "routing_key": "order.created",
    "payload": payload,
    "payload_encoding": "string",
}))
PY
)

echo "Publishing order #${ORDER_ID} (${USER_NAME}, \$${TOTAL}) ..."
RESPONSE=$(curl -s -u guest:guest -H "Content-Type: application/json" \
  -X POST http://localhost:15672/api/exchanges/%2f/store.events/publish \
  -d "$BODY")

echo "Broker response: ${RESPONSE}"

if [[ "$RESPONSE" == *'"routed":true'* ]]; then
  echo "✅ routed:true — message reached the queue. Check the consumer:"
  echo "   docker compose logs notification-service | grep 📧"
else
  echo "❌ Not routed. Is notification-service running (so the queue/binding exist)?"
  exit 1
fi
