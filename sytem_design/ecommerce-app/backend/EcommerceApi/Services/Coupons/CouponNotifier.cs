using EcommerceApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EcommerceApi.Services.Coupons
{
    public interface ICouponNotifier
    {
        Task NotifyAvailableAsync(CouponRules coupon);
    }

    public class CouponNotifier : ICouponNotifier
    {
        private readonly IHubContext<CouponHub> _hub;
        public CouponNotifier(IHubContext<CouponHub> hub) => _hub = hub;

        public Task NotifyAvailableAsync(CouponRules c)
        {
            var message = new { c.Code, c.DiscountType, c.Value, c.MaxDiscount, c.MinOrderAmount, c.ExpiresAt };

            // A coupon assigned to one user goes only to that user's connections (SignalR identifies a user by
            // the NameIdentifier claim, which JwtHelper already sets). A public coupon goes to everyone connected.
            return c.AssignedUserId.HasValue
                ? _hub.Clients.User(c.AssignedUserId.Value.ToString()).SendAsync("CouponAvailable", message)
                : _hub.Clients.All.SendAsync("CouponAvailable", message);
        }
    }
}