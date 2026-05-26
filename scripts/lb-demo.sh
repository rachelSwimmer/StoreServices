#!/usr/bin/env bash
#
# Load-balancer example driver for StoreServices.
#
#   ./scripts/lb-demo.sh distribution   # round-robin spread across 3 replicas
#   ./scripts/lb-demo.sh failover       # stop an instance, traffic keeps flowing
#   ./scripts/lb-demo.sh eviction       # passive health-check takes a bad node out
#   ./scripts/lb-demo.sh algorithm      # round-robin vs least_conn
#   ./scripts/lb-demo.sh all            # all of the above, in order
#
# Prereq: `docker compose up --build` is running.
#
set -euo pipefail

LB_URL="http://localhost:8085/internal/lb/whoami"
INST1="http://localhost:8091/internal/lb"
INST2_TOGGLE="http://localhost:8092/internal/lb/health/toggle"
NGINX_CONF="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/nginx/catalog-lb.conf"
REQUESTS="${REQUESTS:-30}"

hr()  { printf '%s\n' "------------------------------------------------------------"; }
head(){ hr; printf '  %s\n' "$1"; hr; }

# Fire $REQUESTS requests at the LB, print a per-instance tally and the
# count of non-200 responses (the failover/eviction "no client errors" proof).
run_loop() {
    local errors=0
    local -A hits=()
    for _ in $(seq 1 "$REQUESTS"); do
        code=$(curl -s -o /tmp/lb_body -w '%{http_code}' "$LB_URL" || echo "000")
        if [[ "$code" == "200" ]]; then
            inst=$(grep -o '"instance":"[^"]*"' /tmp/lb_body | cut -d'"' -f4)
            hits["${inst:-unknown}"]=$(( ${hits["${inst:-unknown}"]:-0} + 1 ))
        else
            errors=$(( errors + 1 ))
        fi
    done
    echo "Distribution over $REQUESTS requests:"
    for k in $(printf '%s\n' "${!hits[@]}" | sort); do
        printf '  %-22s %s\n' "$k" "${hits[$k]}"
    done
    echo "Non-200 responses: $errors"
}

demo_distribution() {
    head "DISTRIBUTION — load balancer spreads requests across replicas"
    run_loop
}

demo_failover() {
    head "FAILOVER — stop one replica, traffic continues with no client errors"
    echo "Stopping product-catalog-2 ..."
    docker stop product-catalog-2 >/dev/null
    run_loop
    echo
    echo "(expected: 0 non-200, only instances 1 and 3 appear)"
    echo "Restarting product-catalog-2 ..."
    docker start product-catalog-2 >/dev/null
    for _ in $(seq 1 30); do
        [[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8092/internal/lb/whoami || echo 000)" == "200" ]] && break
        sleep 2
    done
    sleep 11   # let nginx fail_timeout expire so it probes the node again
    echo "After restart:"
    run_loop
}

demo_eviction() {
    head "HEALTH-CHECK EVICTION — bad-but-running node is taken out of rotation"
    echo "Marking product-catalog-2 unhealthy (it stays running) ..."
    curl -s -X POST "$INST2_TOGGLE" >/dev/null
    sleep 1
    run_loop
    echo
    docker ps --filter name=product-catalog-2 --format '  still running: {{.Names}} ({{.Status}})'
    echo "(expected: instance-2 absent from rotation though its container is up)"
    echo "Restoring product-catalog-2 to healthy ..."
    curl -s -X POST "$INST2_TOGGLE" >/dev/null
    sleep 11   # let fail_timeout expire so nginx re-adds it
    echo "After restore:"
    run_loop
}

set_least_conn() {
    # $1 = on|off
    if [[ "$1" == "on" ]]; then
        sed -i 's/^\(\s*\)# least_conn;/\1least_conn;/' "$NGINX_CONF"
    else
        sed -i 's/^\(\s*\)least_conn;/\1# least_conn;/' "$NGINX_CONF"
    fi
    docker compose restart nginx-catalog-lb >/dev/null
    sleep 3
}

# Concurrent burst — needed for least_conn to differ from round-robin.
run_burst() {
    local total="${1:-60}" conc="${2:-12}"
    seq 1 "$total" \
      | xargs -P "$conc" -I{} curl -s "$LB_URL" \
      | grep -o '"instance":"[^"]*"' | cut -d'"' -f4 \
      | sort | uniq -c | awk '{printf "  %-22s %s\n", $2, $1}'
}

demo_algorithm() {
    head "ALGORITHM — round-robin vs least_conn (under concurrent load)"
    local slow
    slow=$(curl -s "$INST1/whoami" | grep -o '"instance":"[^"]*"' | cut -d'"' -f4)
    echo "Making instance $slow artificially slow (400ms) ..."
    curl -s -X POST "$INST1/slow/400" >/dev/null

    set_least_conn off
    echo "[round-robin] — assigns evenly regardless of latency:"
    run_burst 60 12
    echo
    set_least_conn on
    echo "[least_conn] — steers away from the slow node ($slow gets fewer):"
    run_burst 60 12

    curl -s -X POST "$INST1/slow/0" >/dev/null   # reset latency
    set_least_conn off                           # restore default algorithm
    echo
    echo "(latency reset; config restored to round-robin)"
}

case "${1:-all}" in
    distribution) demo_distribution ;;
    failover)     demo_failover ;;
    eviction)     demo_eviction ;;
    algorithm)    demo_algorithm ;;
    all)
        demo_distribution
        demo_failover
        demo_eviction
        demo_algorithm
        ;;
    *)
        echo "usage: $0 {distribution|failover|eviction|algorithm|all}" >&2
        exit 1
        ;;
esac
