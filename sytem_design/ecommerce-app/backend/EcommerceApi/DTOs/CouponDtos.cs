using System.ComponentModel.DataAnnotations;

namespace EcommerceApi.DTOs
{
    public class ValidateCouponDto
    {
        public string Code { get; set; } = string.Empty;
        public decimal SubTotal { get; set; }
    }

    public class CouponPreviewDto
    {
        public bool Valid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Code { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public int? CouponId { get; set; }
    }

    // Admin create and edit
    public class CouponDto
    {
        [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
        [Required] public string DiscountType { get; set; } = "Percentage";
        [Range(0.01, 1000000)] public decimal Value { get; set; }
        public decimal? MaxDiscount { get; set; }
        [Range(0, 100000000)] public decimal MinOrderAmount { get; set; }
        public DateTime? StartsAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        [Range(1, 1000000)] public int? UsageLimit { get; set; }
        public bool IsActive { get; set; } = true;
        public string? AssignedUserEmail { get; set; }       // restrict the coupon to one user
    }

    public class CouponResponseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal? MaxDiscount { get; set; }
        public decimal MinOrderAmount { get; set; }
        public DateTime? StartsAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int? UsageLimit { get; set; }
        public int UsedCount { get; set; }
        public bool IsActive { get; set; }
        public int? AssignedUserId { get; set; }
        public string? AssignedUserEmail { get; set; }
        public string Status { get; set; } = string.Empty;   // Active, Scheduled, Expired, Inactive
        public int RedemptionCount { get; set; }
        public decimal DiscountGiven { get; set; }
    }

    // Shown to customers
    public class PublicCouponDto
    {
        public string Code { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal? MaxDiscount { get; set; }
        public decimal MinOrderAmount { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class MyRedemptionDto
    {
        public string Code { get; set; } = string.Empty;
        public int OrderId { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime RedeemedAt { get; set; }
    }

    // One row of a coupon's history for the Admin
    public class RedemptionRowDto
    {
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int OrderId { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime RedeemedAt { get; set; }
    }

    public class DailyUsageDto
    {
        public string Date { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Discount { get; set; }
    }

    public class TopCouponDto
    {
        public string Code { get; set; } = string.Empty;
        public int Redemptions { get; set; }
        public decimal Discount { get; set; }
    }

    public class CouponStatsDto
    {
        public int TotalCoupons { get; set; }
        public int ActiveCoupons { get; set; }
        public int TotalRedemptions { get; set; }
        public decimal TotalDiscountGiven { get; set; }
        public List<DailyUsageDto> Last7Days { get; set; } = new();
        public List<TopCouponDto> TopCoupons { get; set; } = new();
    }

    public class RedeemCouponDto
    {
        [Required]
        public int CouponId { get; set; }
 
        [Required]
        public int OrderId { get; set; }
 
        [Range(0, 100000000)]
        public decimal DiscountAmount { get; set; }
    }
}