using NotificationService.Consumers;
using Serilog;
using SharedKernel.Messaging;
using SharedKernel.Observability;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .ConfigureOtlpLogging("NotificationService")
    .CreateLogger();


try
{
    Log.Information("Starting NotificationService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.AddObservability("NotificationService");

    var rabbitSettings = builder.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>()
                         ?? new RabbitMqSettings();
    builder.Services.AddSingleton(rabbitSettings);
    builder.Services.AddHostedService<OrderCreatedConsumer>();

    var app = builder.Build();

    // No business HTTP API — just a health endpoint for the orchestrator.
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
