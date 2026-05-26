using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Interfaces;
using OrderService.Repositories;
using Serilog;
using SharedKernel.Auth;
using SharedKernel.Messaging;
using SharedKernel.Middleware;

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
    Log.Information("Starting OrderService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "OrderService", Version = "v1" });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token in the format: Bearer {token}"
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    builder.Services.AddDbContext<OrderDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null)));

    builder.Services.AddHttpClient<IUserClient, UserClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:UserAuthService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:CatalogService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IOrderService, OrderService.Services.OrderService>();

    // RabbitMQ publisher (singleton — one long-lived connection shared across requests).
    // Reads the "RabbitMq" config section; falls back to sensible defaults.
    var rabbitSettings = builder.Configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>()
                         ?? new RabbitMqSettings();
    builder.Services.AddSingleton(rabbitSettings);
    builder.Services.AddSingleton<IEventPublisher, RabbitMqPublisher>();

    builder.Services.AddJwtBearerValidation(builder.Configuration);
    builder.Services.AddAuthorization();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var retries = 10;
        while (retries > 0)
        {
            try
            {
                db.Database.Migrate();
                Log.Information("Database migrations applied successfully");
                break;
            }
            catch (Exception ex)
            {
                retries--;
                Log.Warning("Database not ready, retrying in 5s ({Retries} attempts left): {Error}", retries, ex.Message);
                if (retries == 0) throw;
                Thread.Sleep(5000);
            }
        }
    }

    app.UseRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("OrderService is now running");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OrderService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
