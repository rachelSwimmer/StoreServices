using BffService.Clients;
using Microsoft.OpenApi.Models;
using Serilog;
using SharedKernel.Auth;
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
    Log.Information("Starting BffService");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

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

    builder.Services.AddHttpClient<IOrderClient, OrderClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:OrderService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<IUserClient, UserClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:UserAuthService"]!))
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:CatalogService"]!))
        .AddStandardResilienceHandler();

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
