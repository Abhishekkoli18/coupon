using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EcommerceApi.Hubs
{
    // Only logged-in users can connect. The server pushes messages; clients only listen.
    [Authorize]
    public class CouponHub : Hub { }
}