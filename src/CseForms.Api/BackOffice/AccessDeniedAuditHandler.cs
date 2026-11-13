using System.Security.Claims;
using CseForms.Application.BackOffice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Journalise les refus prononcés par la politique d'autorisation elle-même : compte authentifié sans rôle
/// du back-office. Les refus pour rôle insuffisant sur une action (<c>Forbid()</c> d'une page) ne passent pas
/// ici ; ils sont déjà journalisés par les services, avec leur cible.
/// </summary>
internal sealed class AccessDeniedAuditHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>Motif enregistré dans <c>detail</c> ; l'adresse demandée n'est pas conservée.</summary>
    public const string NoRoleDetail = "no-role";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var audit = context.RequestServices.GetRequiredService<IAuditLog>();
            await audit.RecordAsync(AuditActions.AccessDenied, userId, null, NoRoleDetail, context.RequestAborted);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
