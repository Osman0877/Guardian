using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Guardian.Core.Models;

namespace Guardian.Server.Hubs
{
    public class GuardianHub : Hub
    {
        public async Task RegisterDevice(string deviceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"device_{deviceId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, "all_devices");
        }

        public async Task RegisterParent(string familyId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"family_{familyId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, "all_parents");
        }

        public async Task SendDeviceStatus(DeviceStatusRecord status)
        {
            await Clients.Group("all_parents").SendAsync("DeviceStatusUpdated", status);
        }

        public async Task CommandExecuted(string commandId, string status, string? error)
        {
            await Clients.Group("all_parents").SendAsync("CommandStatusChanged", new { commandId, status, error });
        }
    }
}
