using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Observability;

/// <summary>
/// Process-wide handles for *manual* instrumentation — the spans and metrics we
/// emit ourselves, as opposed to the ones the auto-instrumentation libraries
/// emit for ASP.NET Core, HttpClient and EF Core.
///
/// Two things live here:
///  - <see cref="ActivitySource"/>: used to open custom trace spans. We use it
///    to bridge the one hop auto-instrumentation can't see on its own — the
///    RabbitMQ publish/consume across the broker (see <c>RabbitMqPublisher</c>
///    and <c>OrderCreatedConsumer</c>).
///  - <see cref="Meter"/> + counters: custom business metrics, e.g. how many
///    orders were created. Auto-instrumentation gives technical metrics
///    (request rate, latency); this gives domain metrics.
///
/// The names registered here MUST be handed to the tracer/meter providers via
/// <c>AddSource(...)</c> / <c>AddMeter(...)</c> — that wiring is in
/// <see cref="ObservabilityExtensions"/>, so nothing emitted here is dropped.
/// </summary>
public static class DiagnosticsConfig
{
    /// <summary>Logical name grouping all StoreServices telemetry in the backend.</summary>
    public const string ActivitySourceName = "StoreServices.Messaging";
    public const string MeterName = "StoreServices.Business";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    /// <summary>Incremented once per order successfully persisted by OrderService.</summary>
    public static readonly Counter<long> OrdersCreated = Meter.CreateCounter<long>(
        name: "store.orders.created",
        unit: "{order}",
        description: "Number of orders successfully created");
}
