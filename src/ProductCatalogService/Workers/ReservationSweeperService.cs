using ProductCatalogService.Interfaces;

namespace ProductCatalogService.Workers;

public class ReservationSweeperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationSweeperService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public ReservationSweeperService(IServiceScopeFactory scopeFactory, ILogger<ReservationSweeperService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IStockReservationRepository>();
                await repo.ReleaseExpiredAsync();
                _logger.LogDebug("Reservation sweeper completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reservation sweeper");
            }
        }
    }
}
