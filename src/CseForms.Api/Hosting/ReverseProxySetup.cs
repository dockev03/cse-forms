using Microsoft.AspNetCore.HttpOverrides;

namespace CseForms.Api.Hosting;

/// <summary>
/// Derrière Traefik, l'IP du client vient de <c>X-Forwarded-For</c>. Cet en-tête n'est accepté que
/// depuis les réseaux déclarés dans <c>ReverseProxy:KnownNetworks</c> (CIDR séparés par des virgules),
/// sinon n'importe qui pourrait contourner la limitation de débit en le forgeant.
/// </summary>
internal static class ReverseProxySetup
{
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = (configuration["ReverseProxy:KnownNetworks"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(System.Net.IPNetwork.Parse)
            .ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }
}
