using System.Security.Claims;
using EcommerceApi.Data;
using EcommerceApi.DTOs;
using EcommerceApi.Models;
using EcommerceApi.Services.Coupons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EcommerceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CouponController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICouponService _coupons;
        private readonly ICouponNotifier _notifier;

        public CouponController(AppDbContext db, ICouponService coupons, ICouponNotifier notifier)
        {
            _db = db;
            _coupons = coupons;
            _notifier = notifier;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private static string StatusOf(Coupon c, DateTime now)
        {
            if (!c.IsActive) return c.ExpiresAt.HasValue && c.ExpiresAt.Value < now ? "Expired" : "Inactive";
            if (c.StartsAt.HasValue && c.StartsAt.Value > now) return "Scheduled";
            if (c.ExpiresAt.HasValue && c.ExpiresAt.Value < now) return "Expired";
            return "Active";
        }

        private static PublicCouponDto ToPublic(CouponRules c) => new()
        {
            Code = c.Code,
            DiscountType = c.DiscountType,
            Value = c.Value,
            MaxDiscount = c.MaxDiscount,
            MinOrderAmount = c.MinOrderAmount,
            ExpiresAt = c.ExpiresAt
        };

        // ---------- Customers ----------

        // POST /api/coupon/validate   a PREVIEW only, nothing is consumed
        [HttpPost("validate")]
        [EnableRateLimiting("coupon")]
        public async Task<IActionResult> Validate(ValidateCouponDto dto)
        {
            var userId = CurrentUserId;
            var cart = await _db.CartItems.Where(c => c.UserId == userId).Include(c => c.Product).ToListAsync();
            if (cart.Count == 0) return BadRequest(new { message = "Your cart is empty." });

            var subTotal = cart.Sum(c => c.Product!.Price * c.Quantity);
            var result = await _coupons.EvaluateAsync(dto.Code, userId, subTotal);

            return Ok(new CouponPreviewDto
            {
                Valid = result.Success,
                Message = result.Message,
                Code = result.Coupon?.Code,
                CouponId = result.Coupon?.Id,
                SubTotal = result.SubTotal,
                Discount = result.Discount,
                Total = result.Total
            });
        }
        [HttpPost("redeem")]

        public async Task<IActionResult> Redeem([FromBody] RedeemCouponDto dto)

        {

            var userId = CurrentUserId;

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var alreadyRedeemed = await _db.CouponRedemptions.AnyAsync(r => r.OrderId == dto.OrderId);

            if (alreadyRedeemed)

            {

                return Ok(new

                {

                    message = "Coupon already redeemed for this order."

                });

            }

            var rows = await _db.Coupons.Where(c => c.Id == dto.CouponId &&
            c.IsActive && (c.UsageLimit == null || c.UsedCount < c.UsageLimit)).ExecuteUpdateAsync(s =>
                            s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1));

            if (rows == 0)

            {

                return BadRequest(new

                {

                    message = "Coupon is no longer available."

                });

            }

            _db.CouponRedemptions.Add(new CouponRedemption

            {

                CouponId = dto.CouponId,

                UserId = userId,

                OrderId = dto.OrderId,

                DiscountAmount = dto.DiscountAmount

            });

            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            return Ok(new

            {

                message = "Coupon redeemed successfully."

            });

        }


        // GET /api/coupon/public   the SAME list for everyone, so it is safe to output-cache
        [HttpGet("public")]
        [AllowAnonymous]
        [OutputCache(PolicyName = "CouponsPublic")]
        public async Task<IActionResult> GetPublic()
        {
            var now = DateTime.UtcNow;
            var list = (await _coupons.GetActiveCouponsAsync())
                .Where(c => c.AssignedUserId == null
                         && (c.StartsAt == null || c.StartsAt <= now)
                         && (c.ExpiresAt == null || c.ExpiresAt > now))
                .Select(ToPublic)
                .ToList();
            return Ok(list);
        }

        // GET /api/coupon/mine   coupons assigned to ME that I have not used yet (per user, so NOT output-cached)
        [HttpGet("mine")]
        public async Task<IActionResult> GetMine()
        {
            var userId = CurrentUserId;
            var now = DateTime.UtcNow;
            var usedIds = await _db.CouponRedemptions.Where(r => r.UserId == userId).Select(r => r.CouponId).ToListAsync();

            var list = (await _coupons.GetActiveCouponsAsync())
                .Where(c => c.AssignedUserId == userId
                         && !usedIds.Contains(c.Id)
                         && (c.StartsAt == null || c.StartsAt <= now)
                         && (c.ExpiresAt == null || c.ExpiresAt > now))
                .Select(ToPublic)
                .ToList();
            return Ok(list);
        }

        // GET /api/coupon/my-redemptions   my own history
        [HttpGet("my-redemptions")]
        public async Task<IActionResult> MyRedemptions()
        {
            var userId = CurrentUserId;
            var list = await _db.CouponRedemptions
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RedeemedAt)
                .Select(r => new MyRedemptionDto
                {
                    Code = r.Coupon!.Code,
                    OrderId = r.OrderId,
                    DiscountAmount = r.DiscountAmount,
                    RedeemedAt = r.RedeemedAt
                })
                .ToListAsync();
            return Ok(list);
        }

        // ---------- Admin ----------

        // Shared checks for create and edit. Returns an error text, or the resolved assigned user id.
        private async Task<(string? Error, int? AssignedUserId)> ValidateRulesAsync(CouponDto dto)
        {
            if (dto.DiscountType != "Percentage" && dto.DiscountType != "Fixed")
                return ("Type must be Percentage or Fixed.", null);
            if (dto.DiscountType == "Percentage" && dto.Value > 100)
                return ("A percentage cannot be more than 100.", null);
            if (dto.StartsAt.HasValue && dto.ExpiresAt.HasValue && dto.ExpiresAt <= dto.StartsAt)
                return ("Expiry must be after the start date.", null);

            int? assigned = null;
            if (!string.IsNullOrWhiteSpace(dto.AssignedUserEmail))
            {
                var email = dto.AssignedUserEmail.Trim();
                assigned = await _db.Users.Where(u => u.Email == email).Select(u => (int?)u.Id).FirstOrDefaultAsync();
                if (assigned == null) return ($"No user found with email {email}.", null);
            }
            return (null, assigned);
        }

        // GET /api/coupon   list with status, usage and discount given
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var now = DateTime.UtcNow;
            var coupons = await _db.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync();

            var usage = await _db.CouponRedemptions
                .GroupBy(r => r.CouponId)
                .Select(g => new { CouponId = g.Key, Count = g.Count(), Discount = g.Sum(r => r.DiscountAmount) })
                .ToDictionaryAsync(x => x.CouponId);

            var userIds = coupons.Where(c => c.AssignedUserId.HasValue).Select(c => c.AssignedUserId!.Value).Distinct().ToList();
            var emails = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

            var result = coupons.Select(c => new CouponResponseDto
            {
                Id = c.Id,
                Code = c.Code,
                DiscountType = c.DiscountType,
                Value = c.Value,
                MaxDiscount = c.MaxDiscount,
                MinOrderAmount = c.MinOrderAmount,
                StartsAt = c.StartsAt,
                ExpiresAt = c.ExpiresAt,
                UsageLimit = c.UsageLimit,
                UsedCount = c.UsedCount,
                IsActive = c.IsActive,
                AssignedUserId = c.AssignedUserId,
                AssignedUserEmail = c.AssignedUserId.HasValue && emails.TryGetValue(c.AssignedUserId.Value, out var email) ? email : null,
                Status = StatusOf(c, now),
                RedemptionCount = usage.TryGetValue(c.Id, out var u1) ? u1.Count : 0,
                DiscountGiven = usage.TryGetValue(c.Id, out var u2) ? u2.Discount : 0m
            }).ToList();

            return Ok(result);
        }

        // GET /api/coupon/stats   the dashboard numbers
        [HttpGet("stats")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Stats()
        {
            var now = DateTime.UtcNow;
            var since = now.Date.AddDays(-6);

            var perDay = await _db.CouponRedemptions
                .Where(r => r.RedeemedAt >= since)
                .GroupBy(r => r.RedeemedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count(), Discount = g.Sum(r => r.DiscountAmount) })
                .ToListAsync();

            var top = await _db.CouponRedemptions
                .GroupBy(r => r.CouponId)
                .Select(g => new { CouponId = g.Key, Count = g.Count(), Discount = g.Sum(r => r.DiscountAmount) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();
            var topIds = top.Select(t => t.CouponId).ToList();
            var codes = await _db.Coupons.Where(c => topIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code);

            return Ok(new CouponStatsDto
            {
                TotalCoupons = await _db.Coupons.CountAsync(),
                ActiveCoupons = await _db.Coupons.CountAsync(c => c.IsActive
                    && (c.StartsAt == null || c.StartsAt <= now) && (c.ExpiresAt == null || c.ExpiresAt > now)),
                TotalRedemptions = await _db.CouponRedemptions.CountAsync(),
                TotalDiscountGiven = await _db.CouponRedemptions.SumAsync(r => (decimal?)r.DiscountAmount) ?? 0m,
                Last7Days = Enumerable.Range(0, 7).Select(i =>
                {
                    var day = since.AddDays(i);
                    var match = perDay.FirstOrDefault(x => x.Day.Date == day);
                    return new DailyUsageDto
                    {
                        Date = day.ToString("yyyy-MM-dd"),
                        Count = match?.Count ?? 0,
                        Discount = match?.Discount ?? 0m
                    };
                }).ToList(),
                TopCoupons = top.Select(t => new TopCouponDto
                {
                    Code = codes.GetValueOrDefault(t.CouponId, "?"),
                    Redemptions = t.Count,
                    Discount = t.Discount
                }).ToList()
            });
        }

        // GET /api/coupon/5/redemptions?pageNumber=1   who used this coupon
        [HttpGet("{id:int}/redemptions")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Redemptions(int id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _db.CouponRedemptions.Where(r => r.CouponId == id);
            var total = await query.CountAsync();

            var rows = await query
                .Join(_db.Users, r => r.UserId, u => u.Id, (r, u) => new { r, u })
                .OrderByDescending(x => x.r.RedeemedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new RedemptionRowDto
                {
                    UserName = x.u.FullName,
                    UserEmail = x.u.Email,
                    OrderId = x.r.OrderId,
                    DiscountAmount = x.r.DiscountAmount,
                    RedeemedAt = x.r.RedeemedAt
                })
                .ToListAsync();

            return Ok(new PagedResultDto<RedemptionRowDto>
            {
                Items = rows,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        // POST /api/coupon
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(CouponDto dto)
        {
            var (error, assignedUserId) = await ValidateRulesAsync(dto);
            if (error != null) return BadRequest(new { message = error });

            var code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Coupons.AnyAsync(c => c.Code == code))
                return Conflict(new { message = "A coupon with this code already exists." });

            var now = DateTime.UtcNow;
            var coupon = new Coupon
            {
                Code = code,
                DiscountType = dto.DiscountType,
                Value = dto.Value,
                MaxDiscount = dto.MaxDiscount,
                MinOrderAmount = dto.MinOrderAmount,
                StartsAt = dto.StartsAt,
                ExpiresAt = dto.ExpiresAt,
                UsageLimit = dto.UsageLimit,
                IsActive = dto.IsActive,
                AssignedUserId = assignedUserId
            };

            // Live right now: announce immediately. Otherwise the background job announces it at the start time.
            var availableNow = coupon.IsActive && (coupon.StartsAt == null || coupon.StartsAt <= now);
            if (availableNow) coupon.NotifiedAt = now;

            _db.Coupons.Add(coupon);
            await _db.SaveChangesAsync();
            await _coupons.InvalidateAsync();

            if (availableNow) await _notifier.NotifyAvailableAsync(CouponRules.From(coupon));

            return Ok(new { message = $"Coupon {code} created." });
        }

        // PUT /api/coupon/5
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, CouponDto dto)
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon == null) return NotFound(new { message = "Coupon not found." });

            var (error, assignedUserId) = await ValidateRulesAsync(dto);
            if (error != null) return BadRequest(new { message = error });

            var code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Coupons.AnyAsync(c => c.Code == code && c.Id != id))
                return Conflict(new { message = "Another coupon already uses this code." });

            if (dto.UsageLimit.HasValue && dto.UsageLimit.Value < coupon.UsedCount)
                return BadRequest(new { message = $"The limit cannot be lower than the {coupon.UsedCount} uses already recorded." });

            coupon.Code = code;
            coupon.DiscountType = dto.DiscountType;
            coupon.Value = dto.Value;
            coupon.MaxDiscount = dto.MaxDiscount;
            coupon.MinOrderAmount = dto.MinOrderAmount;
            coupon.StartsAt = dto.StartsAt;
            coupon.ExpiresAt = dto.ExpiresAt;
            coupon.UsageLimit = dto.UsageLimit;
            coupon.IsActive = dto.IsActive;
            coupon.AssignedUserId = assignedUserId;
            coupon.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            try
            {
                await _coupons.InvalidateAsync();
            }
            catch
            {

            }
            return Ok(new { message = $"Coupon {code} updated." });
        }

        // POST /api/coupon/5/toggle   deactivate or activate (never delete: history refers to it)
        [HttpPost("{id:int}/toggle")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Toggle(int id)
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon == null) return NotFound(new { message = "Coupon not found." });

            coupon.IsActive = !coupon.IsActive;
            coupon.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _coupons.InvalidateAsync();
            return Ok(new { message = coupon.IsActive ? "Coupon activated." : "Coupon deactivated." });
        }
        // DELETE /api/coupon/5
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var coupon = await _db.Coupons
                .FirstOrDefaultAsync(c => c.Id == id);

            if (coupon == null)
            {
                return NotFound(new
                {
                    message = "Coupon not found."
                });
            }

            // Check whether this coupon has redemption history
            var hasRedemptions = await _db.CouponRedemptions
                .AnyAsync(r => r.CouponId == id);

            if (hasRedemptions)
            {
                return Conflict(new
                {
                    message = "This coupon has redemption history and cannot be deleted. Please deactivate it instead."
                });
            }

            _db.Coupons.Remove(coupon);

            await _db.SaveChangesAsync();

            await _coupons.InvalidateAsync();

            return Ok(new
            {
                message = $"Coupon {coupon.Code} deleted successfully."
            });
        }
    }
}
