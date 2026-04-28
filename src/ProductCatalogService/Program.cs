using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.OpenApi.Models;
using ProductCatalogService.Data;
using ProductCatalogService.Interfaces;
using ProductCatalogService.Repositories;
using ProductCatalogService.Services;
using Serilog;
using SharedKernel.Auth;
using SharedKernel.Middleware;
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
    .CreateLogger();

try
{
    Log.Information("Starting ProductCatalogService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "ProductCatalogService", Version = "v1" });
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

    builder.Services.AddDbContext<CatalogDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null)));

    var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
    redisConfig.AbortOnConnectFail = false;
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisConfig);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult((IConnectionMultiplexer)redisMultiplexer);
        options.InstanceName = builder.Configuration.GetValue<string>("Cache:InstanceName", "Catalog:");
    });

    builder.Services.AddSingleton<ICacheService, CacheService>();

    builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
    builder.Services.AddScoped<IProductRepository, ProductRepository>();
    builder.Services.AddScoped<IStockReservationRepository, StockReservationRepository>();
    builder.Services.AddHostedService<ProductCatalogService.Workers.ReservationSweeperService>();

    builder.Services.AddScoped<CategoryService>();
    builder.Services.AddScoped<ProductService>();

    builder.Services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
        sp.GetRequiredService<CategoryService>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ILogger<CachedCategoryService>>(),
        sp.GetRequiredService<IConfiguration>()));

    builder.Services.AddScoped<IProductService>(sp => new CachedProductService(
        sp.GetRequiredService<ProductService>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ILogger<CachedProductService>>(),
        sp.GetRequiredService<IConfiguration>()));

    builder.Services.AddJwtBearerValidation(builder.Configuration);
    builder.Services.AddAuthorization();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
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

    Log.Information("ProductCatalogService is now running");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ProductCatalogService terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
