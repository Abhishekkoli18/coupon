using EcommerceApi.Models;

namespace EcommerceApi.Services.Coupons
{
    // A small, serializable snapshot of a coupon's RULES. It has no usage counter,
    // so it is safe to keep in a cache for a few minutes.
    public record CouponRules(
        int Id, string Code, string DiscountType, decimal Value, decimal? MaxDiscount,
        decimal MinOrderAmount, DateTime? StartsAt, DateTime? ExpiresAt,
        int? UsageLimit, int? AssignedUserId, bool IsActive)
    {
        public static CouponRules From(Coupon c) => new(
            c.Id, c.Code, c.DiscountType, c.Value, c.MaxDiscount, c.MinOrderAmount,
            c.StartsAt, c.ExpiresAt, c.UsageLimit, c.AssignedUserId, c.IsActive);
    }
}