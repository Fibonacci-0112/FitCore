using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Billing;

/// <summary>Entitlements (RevenueCat) and coach billing (Stripe Connect). Owns the <c>billing</c> schema.</summary>
public sealed class BillingModule : IModule
{
    public string Name => "Billing";

    public string Schema => "billing";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
