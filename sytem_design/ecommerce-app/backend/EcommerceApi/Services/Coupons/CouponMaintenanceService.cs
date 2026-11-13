using EcommerceApi.Data;
using Microsoft.EntityFrameworkCore;

namespace EcommerceApi.Services.Coupons
{
    public class CouponMaintenanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CouponMaintenanceService> _logger;

        public CouponMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<CouponMaintenanceService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));   // use seconds while testing
            try
            {
                do
                {
                    try { await RunOnceAsync(stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Coupon maintenance failed");   // a failure must not kill the loop
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) { /* the app is shutting down */ }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();        // AppDbContext is Scoped; this class lives forever
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var coupons = scope.ServiceProvider.GetRequiredService<ICouponService>();
            var notifier = scope.ServiceProvider.GetRequiredService<ICouponNotifier>();
            var now = DateTime.UtcNow;

            // 1. Expire: one UPDATE statement, no rows loaded
            var expired = await db.Coupons
                .Where(c => c.IsActive && c.ExpiresAt != null && c.ExpiresAt < now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsActive, false)
                    .SetProperty(c => c.UpdatedAt, now), ct);

            // 2. Announce coupons that just became available (a scheduled start time arrived)
            var due = await db.Coupons
                .Where(c => c.IsActive && c.NotifiedAt == null
                         && (c.StartsAt == null || c.StartsAt <= now)
                         && (c.ExpiresAt == null || c.ExpiresAt > now))
                .ToListAsync(ct);

            foreach (var coupon in due)
            {
                await notifier.NotifyAvailableAsync(CouponRules.From(coupon));
                coupon.NotifiedAt = now;
            }
            if (due.Count > 0) await db.SaveChangesAsync(ct);

            if (expired > 0 || due.Count > 0)
            {
                await coupons.InvalidateAsync();
                _logger.LogInformation("Coupon maintenance: {Expired} expired, {Announced} announced", expired, due.Count);
            }
        }
    }
}