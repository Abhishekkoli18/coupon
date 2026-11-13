using EcommerceApi.Data;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace EcommerceApi.Services.Coupons
{
    public record CouponResult(bool Success, string Message, CouponRules? Coupon, decimal SubTotal, decimal Discount)
    {
        public decimal Total => SubTotal - Discount;
    }

    public interface ICouponService
    {
        Task<CouponResult> EvaluateAsync(string? code, int userId, decimal subTotal);
        Task<List<CouponRules>> GetActiveCouponsAsync();
        Task InvalidateAsync();
    }

    public class CouponService : ICouponService
    {
        private const string ActiveKey = "coupons:active";

        private readonly AppDbContext _db;
        private readonly HybridCache _cache;
        private readonly IOutputCacheStore _outputCache;

        public CouponService(AppDbContext db, HybridCache cache, IOutputCacheStore outputCache)
        {
            _db = db;
            _cache = cache;
            _outputCache = outputCache;
        }

        // Every coupon with IsActive = true. Cached 5 minutes (1 minute in each server's memory, then Redis).
        public async Task<List<CouponRules>> GetActiveCouponsAsync() =>
            await _cache.GetOrCreateAsync(
                ActiveKey,
                async ct => await _db.Coupons.AsNoTracking()
                    .Where(c => c.IsActive)
                    .Select(c => new CouponRules(c.Id, c.Code, c.DiscountType, c.Value, c.MaxDiscount,
                        c.MinOrderAmount, c.StartsAt, c.ExpiresAt, c.UsageLimit, c.AssignedUserId, c.IsActive))
                    .ToListAsync(ct),
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(5),
                    LocalCacheExpiration = TimeSpan.FromMinutes(1)
                });

        // Call after ANY coupon change: clears the cached list and the cached HTTP responses
        public async Task InvalidateAsync()
        {
            await _cache.RemoveAsync(ActiveKey);
            await _outputCache.EvictByTagAsync("coupons", default);
        }

        public async Task<CouponResult> EvaluateAsync(string? code, int userId, decimal subTotal)
        {
            CouponResult Fail(string message) => new(false, message, null, subTotal, 0);

            if (string.IsNullOrWhiteSpace(code))
                return new CouponResult(true, string.Empty, null, subTotal, 0);

            var normalized = code.Trim().ToUpperInvariant();
            var coupon = (await GetActiveCouponsAsync()).FirstOrDefault(c => c.Code == normalized);   // no SQL on a cache hit
            var now = DateTime.UtcNow;

            // one generic message for unknown, inactive and not-yours: it reveals nothing about which codes exist
            if (coupon == null || (coupon.AssignedUserId.HasValue && coupon.AssignedUserId != userId))
                return Fail("Invalid or expired coupon code.");

            if (coupon.StartsAt.HasValue && now < coupon.StartsAt.Value) return Fail("This coupon is not active yet.");
            if (coupon.ExpiresAt.HasValue && now > coupon.ExpiresAt.Value) return Fail("This coupon has expired.");
            if (subTotal < coupon.MinOrderAmount) return Fail($"Minimum order amount is ₹{coupon.MinOrderAmount:0.##}.");

            // The usage counter changes all the time, so it is NEVER read from the cache
            if (coupon.UsageLimit.HasValue)
            {
                var used = await _db.Coupons.Where(c => c.Id == coupon.Id).Select(c => c.UsedCount).FirstOrDefaultAsync();
                if (used >= coupon.UsageLimit.Value) return Fail("This coupon has been fully used.");
            }

            if (await _db.CouponRedemptions.AnyAsync(r => r.CouponId == coupon.Id && r.UserId == userId))
                return Fail("You have already used this coupon.");

            var discount = coupon.DiscountType == "Percentage"
                ? Math.Round(subTotal * coupon.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : coupon.Value;

            if (coupon.MaxDiscount.HasValue) discount = Math.Min(discount, coupon.MaxDiscount.Value);
            discount = Math.Min(discount, subTotal - 1m);
            if (discount < 0) discount = 0;

            return new CouponResult(true, $"Coupon {coupon.Code} applied.", coupon, subTotal, discount);
        }
    }
}