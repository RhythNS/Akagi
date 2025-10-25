using Akagi.Utils;
using Akagi.Web.Services.Circuits;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Users;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.FileProviders;

namespace Akagi.Web.Services;

static class DependendyInjection
{
    public static void AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserState, UserState>();
        services.AddScoped<ITokenService, TokenService>();

        Type[] transmissionTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<SocketTransmissionHandler>();
        Array.ForEach(transmissionTypes, transmissionType => services.AddTransient(typeof(SocketTransmissionHandler), transmissionType));

        services.Configure<SocketService.Options>(configuration.GetSection("Socket"));
        services.AddSingleton<ISocketService, SocketService>();
        services.AddScoped<ISocketClientContainer, SocketClientContainer>();
        services.AddScoped<IFileProvider, SocketFileProvider>();

        services.AddScoped<ICircuitIdAccessor, CircuitIdAccessor>();
        services.AddScoped<CircuitHandler, CircuitIdHandler>();
    }
}
