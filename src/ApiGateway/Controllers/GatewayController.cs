using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GatewayController : ControllerBase
{
    private readonly ILogger<GatewayController> _logger;
    private readonly IConnectionMultiplexer? _redis;

    public GatewayController(ILogger<GatewayController> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            gateway = "API Gateway",
            status = "Running",
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            services = new[]
            {
                new { name = "ProductCatalogService", url = "http://localhost:5149" },
                new { name = "UserAuthService", url = "http://localhost:5019" }
            }
        });
    }

    [HttpGet("routes")]
    public IActionResult GetRoutes()
    {
        var routes = new object[]
        {
            new { 
                upstream = "/api/products/**", 
                downstream = "ProductCatalogService:5149", 
                requiresAuth = true,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5149/swagger",
                examples = new
                {
                    get_all = "GET /api/products",
                    get_by_id = "GET /api/products/1", 
                    create = "POST /api/products"
                }
            },
            new { 
                upstream = "/api/categories/**", 
                downstream = "ProductCatalogService:5149", 
                requiresAuth = true,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5149/swagger",
                examples = new
                {
                    get_all = "GET /api/categories",
                    get_by_id = "GET /api/categories/1",
                    create = "POST /api/categories"
                }
            },
            new { 
                upstream = "/api/internal/products/**", 
                downstream = "ProductCatalogService:5149", 
                requiresAuth = false,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5149/swagger",
                examples = new
                {
                    get_all = "GET /api/internal/products",
                    get_by_id = "GET /api/internal/products/1",
                    create = "POST /api/internal/products"
                }
            },
            new { 
                upstream = "/api/auth/**", 
                downstream = "UserAuthService:5019", 
                requiresAuth = false,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5019/swagger",
                examples = new
                {
                    login = "POST /api/auth/login",
                    register = "POST /api/auth/register",
                    refresh = "POST /api/auth/refresh"
                }
            },
            new { 
                upstream = "/api/users/**", 
                downstream = "UserAuthService:5019", 
                requiresAuth = true,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5019/swagger",
                examples = new
                {
                    get_profile = "GET /api/users/profile",
                    update_profile = "PUT /api/users/profile",
                    get_all = "GET /api/users"
                }
            },
            new { 
                upstream = "/api/internal/users/**", 
                downstream = "UserAuthService:5019", 
                requiresAuth = false,
                methods = new[] { "GET", "POST", "PUT", "DELETE" },
                swagger = "http://localhost:5019/swagger",
                examples = new
                {
                    get_all = "GET /api/internal/users",
                    get_by_id = "GET /api/internal/users/1",
                    create = "POST /api/internal/users"
                }
            }
        };

        return Ok(new
        {
            routes = routes,
            timestamp = DateTime.UtcNow,
            note = "For full API documentation, visit the Swagger URL for each service"
        });
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var healthStatus = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            uptime = Environment.TickCount64,
            redis = await GetRedisStatus(),
            services = new[]
            {
                new { name = "ProductCatalogService", url = "http://localhost:5149", status = "Unknown" },
                new { name = "UserAuthService", url = "http://localhost:5019", status = "Unknown" }
            }
        };
        
        return Ok(healthStatus);
    }

    [HttpGet("examples")]
    public IActionResult GetApiExamples()
    {
        return Ok(new
        {
            message = "API Usage Examples - all requests go through gateway (localhost:5000)",
            authentication = new
            {
                description = "Get JWT token first, then use 'Authorization: Bearer <token>' header",
                register = new
                {
                    url = "/api/auth/register",
                    method = "POST",
                    body = new
                    {
                        email = "user@example.com",
                        password = "Password123!",
                        firstName = "John",
                        lastName = "Doe"
                    }
                },
                login = new
                {
                    url = "/api/auth/login", 
                    method = "POST",
                    body = new { email = "user@example.com", password = "Password123!" }
                }
            },
            api_examples = new
            {
                products = new
                {
                    get_all = "GET /api/products",
                    get_by_id = "GET /api/products/1",
                    create = new
                    {
                        url = "POST /api/products",
                        auth_required = true,
                        body = new
                        {
                            name = "New Product",
                            price = 29.99,
                            description = "Product description",
                            categoryId = 1
                        }
                    }
                },
                categories = new
                {
                    get_all = "GET /api/categories",
                    create = new
                    {
                        url = "POST /api/categories", 
                        auth_required = true,
                        body = new { name = "Electronics", description = "Electronic products" }
                    }
                },
                internal_access = new
                {
                    products_no_auth = "GET /api/internal/products",
                    users_no_auth = "GET /api/internal/users"
                }
            },
            swagger_links = new
            {
                gateway_management = "http://localhost:5000/swagger",
                product_catalog_full_api = "http://localhost:5149/swagger", 
                user_auth_full_api = "http://localhost:5019/swagger"
            },
            timestamp = DateTime.UtcNow
        });
    }

    private async Task<object> GetRedisStatus()
    {
        if (_redis == null)
        {
            return new { status = "Disabled", message = "Redis not configured" };
        }

        try
        {
            if (_redis.IsConnected)
            {
                var db = _redis.GetDatabase();
                var pong = await db.PingAsync();
                return new { status = "Connected", ping = $"{pong.TotalMilliseconds:F2}ms" };
            }
            else
            {
                return new { status = "Disconnected", message = "Redis connection lost" };
            }
        }
        catch (Exception ex)
        {
            return new { status = "Error", message = ex.Message };
        }
    }
}