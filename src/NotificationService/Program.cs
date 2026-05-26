using NotificationService.Consumers;
using Serilog;
using SharedKernel.Messaging;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();


try
{
    Log.Information("Starting NotificationService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Bind RabbitMq settings and register the consumer as a hosted background
    // service — same pattern as ProductCatalogService's ReservationSweeperService.
    var rabbitSettings = builder.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>()
                         ?? new RabbitMqSettings();
    builder.Services.AddSingleton(rabbitSettings);
    builder.Services.AddHostedService<OrderCreatedConsumer>();

    var app = builder.Build();

    // This service has no business HTTP API — just a health endpoint so the
    // container/orchestrator can tell it's alive.
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "NotificationService" }));

    Log.Information("NotificationService is now running");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NotificationService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
