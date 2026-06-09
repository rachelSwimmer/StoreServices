using BffService.Clients;
using BffService.Composers;
using Microsoft.OpenApi.Models;
using Serilog;
using SharedKernel.Auth;
using SharedKernel.Caching;
using SharedKernel.Middleware;
using SharedKernel.Observability;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .ConfigureOtlpLogging("BffService")
    .CreateLogger();

try
{
    Log.Information("Starting BffService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.AddObservability("BffService");

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "BffService", Version = "v1" });
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

    var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6380";
    var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
    redisConfig.AbortOnConnectFail = false;
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisConfig);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult((IConnectionMultiplexer)redisMultiplexer);
        options.InstanceName = builder.Configuration.GetValue<string>("Cache:InstanceName", "Bff:");
    });

    builder.Services.AddSingleton<ICacheService, CacheService>();

    builder.Services.AddHttpClient<IOrderClient, OrderClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:OrderService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<UserClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:UserAuthService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:CatalogService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddScoped<IUserClient>(sp => new CachedUserClient(
        sp.GetRequiredService<UserClient>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ILogger<CachedUserClient>>(),
        sp.GetRequiredService<IConfiguration>()));

    builder.Services.AddScoped<ICatalogClient>(sp => new CachedCatalogClient(
        sp.GetRequiredService<CatalogClient>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ILogger<CachedCatalogClient>>(),
        sp.GetRequiredService<IConfiguration>()));

    builder.Services.AddScoped<IOrderDetailComposer, OrderDetailComposer>();

    builder.Services.AddJwtBearerValidation(builder.Configuration);
    builder.Services.AddAuthorization();

    var app = builder.Build();

    app.UseRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("BffService is now running");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "BffService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
