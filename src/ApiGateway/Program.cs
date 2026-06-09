using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Serilog;
using System.Text;
using SharedKernel.Middleware;
using SharedKernel.Observability;
using StackExchange.Redis;
using ApiGateway.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/apigateway-.log", rollingInterval: RollingInterval.Day)
    .ConfigureOtlpLogging("ApiGateway")
    .CreateLogger();

builder.Host.UseSerilog();

builder.AddObservability("ApiGateway");

// Add configuration
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    // Load the environment-specific Ocelot file directly. We deliberately do NOT
    // use .AddOcelot(env): that overload merges all ocelot.*.json files and
    // EXCLUDES ocelot.{env}.json, which caused Production to load the Development
    // (localhost) routes and fail with 502 inside Docker.
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrEmpty(jwtKey))
    throw new ArgumentException("JWT Key is not configured");

// Configure Redis (optional)
IConnectionMultiplexer? redisMultiplexer = null;
try
{
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
    redisConfig.AbortOnConnectFail = false;
    redisConfig.ConnectTimeout = 1000; // 1 second timeout
    redisMultiplexer = ConnectionMultiplexer.Connect(redisConfig);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
    Log.Information("Redis connected successfully");
}
catch (Exception ex)
{
    Log.Warning("Redis connection failed: {Error}. Rate limiting will be disabled.", ex.Message);
}

// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// Configure Ocelot
builder.Services.AddOcelot(builder.Configuration);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

// Add controllers for health checks
builder.Services.AddControllers();
builder.Services.AddScoped<GatewayController>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Store Services API Gateway",
        Version = "v1",
        Description = "API Gateway for Store Services microservices architecture"
    });
    
    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Store Services API Gateway V1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowAll");
app.UseRouting();

// Custom middleware to handle direct endpoints before Ocelot
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health")
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { status = "Healthy", timestamp = DateTime.UtcNow }));
        return;
    }
    if (context.Request.Path == "/api/discovery")
    {
        context.Response.ContentType = "application/json";
        var discovery = new
        {
            services = new[]
            {
                new { name = "ProductCatalog", url = "http://localhost:5149", health = "/health" },
                new { name = "UserAuth", url = "http://localhost:5019", health = "/health" }
            },
            timestamp = DateTime.UtcNow
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(discovery));
        return;
    }
    
    // Handle Gateway Management endpoints before Ocelot
    if (context.Request.Path.StartsWithSegments("/api/gateway"))
    {
        await next(); // Let controllers handle these
        return;
    }
    
    await next();
});

// Add custom middleware
app.UseMiddleware<RequestLoggingMiddleware>();

// Only add rate limiting if Redis is available
if (redisMultiplexer != null && redisMultiplexer.IsConnected)
{
    app.UseMiddleware<RateLimitingMiddleware>();
    Log.Information("Rate limiting middleware enabled");
}
else
{
    Log.Warning("Rate limiting middleware disabled - Redis not available");
}

app.UseAuthentication();
app.UseAuthorization();

// UseEndpoints here forces controller execution before Ocelot, which is terminal and never calls next() on 404.
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

await app.UseOcelot();

try
{
    Log.Information("Starting API Gateway");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Gateway terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}