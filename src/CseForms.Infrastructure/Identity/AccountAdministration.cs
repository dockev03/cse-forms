using System.Security.Cryptography;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Identity;

public sealed record AccountSummary(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool TwoFactorEnabled,
    bool Disabled,
    bool MustChangePassword,
    IReadOnlyList<string> FormSlugs);

public sealed record AccountDetail(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool TwoFactorEnabled,
    bool Disabled,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> FormIds);

public sealed record FormChoice(Guid Id, string Slug, string Title);

/// <summary>Résultat d'une action : erreurs à afficher, ou mot de passe provisoire à transmettre une seule fois.</summary>
public sealed record AccountResult(IReadOnlyList<string> Errors, Guid? UserId = null, string? TemporaryPassword = null)
{
    public bool Succeeded => Errors.Count == 0;

    public static AccountResult Fail(params string[] errors) => new(errors);
}

/// <summary>
/// Gestion des comptes des élus par un administrateur (back-office et ligne de commande).
/// Garde-fous : un administrateur ne peut ni se désactiver ni se retirer le rôle, et il reste toujours
/// au moins un administrateur actif. Chaque changement est journalisé, sans donnée personnelle dans le détail.
/// </summary>
public sealed class AccountAdministration(
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    CseFormsDbContext db,
    IAuditLog audit,
    TimeProvider timeProvider)
{
    // Sans caractères ambigus (0/O, 1/l/I) : le mot de passe provisoire se dicte de vive voix.
    private const string TemporaryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public const string SelfChangeRefused = "Vous ne pouvez pas modifier votre propre rôle ni désactiver votre propre compte.";
    public const string LastAdminRefused = "Il doit rester au moins un administrateur actif.";

    /// <summary>Mot de passe provisoire de 19 caractères (4 groupes de 4 séparés par des tirets), environ 79 bits.</summary>
    public static string GenerateTemporaryPassword() =>
        string.Join('-', Enumerable.Range(0, 4).Select(_ => RandomNumberGenerator.GetString(TemporaryAlphabet, 4)));

    public async Task EnsureRolesAsync()
    {
        foreach (var roleName in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                await roles.CreateAsync(new AppRole { Name = roleName });
            }
        }
    }

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var roleByUser = await RolesByUserAsync(cancellationToken);
        var formsByUser = (await db.UserFormAccess
                .Join(db.Forms, a => a.FormId, f => f.Id, (a, f) => new { a.UserId, f.Slug })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.UserId, x => x.Slug);

        return accounts
            .Select(u => new AccountSummary(
                u.Id,
                u.Email!,
                u.DisplayName,
                roleByUser.GetValueOrDefault(u.Id, string.Empty),
                u.TwoFactorEnabled,
                u.DisabledAt is not null,
                u.MustChangePassword,
                formsByUser[u.Id].Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    public async Task<AccountDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return null;
        }

        var formIds = await db.UserFormAccess.Where(a => a.UserId == id).Select(a => a.FormId).ToListAsync(cancellationToken);
        return new AccountDetail(
            user.Id,
            user.Email!,
            user.DisplayName,
            (await RolesByUserAsync(cancellationToken)).GetValueOrDefault(user.Id, string.Empty),
            user.TwoFactorEnabled,
            user.DisabledAt is not null,
            user.MustChangePassword,
            user.CreatedAt,
            formIds);
    }

    public async Task<IReadOnlyList<FormChoice>> ListFormsAsync(CancellationToken cancellationToken) =>
        await db.Forms.OrderBy(f => f.Title).Select(f => new FormChoice(f.Id, f.Slug, f.Title)).ToListAsync(cancellationToken);

    /// <summary>Crée un compte avec un mot de passe provisoire tiré au hasard, à changer à la première connexion.</summary>
    public async Task<AccountResult> CreateAsync(
        Guid actorId,
        string? email,
        string? displayName,
        string? role,
        IReadOnlyCollection<Guid> formIds,
        CancellationToken cancellationToken)
    {
        var password = GenerateTemporaryPassword();
        var result = await CreateCoreAsync(email, displayName, role, password, mustChangePassword: true);
        if (!result.Succeeded)
        {
            return result;
        }

        var userId = result.UserId!.Value;
        await SetFormsAsync(userId, role == AppRoles.Admin ? [] : formIds, cancellationToken);
        await audit.RecordAsync(AuditActions.UserCreated, actorId, $"user:{userId}", $"role:{role}", cancellationToken);
        return result with { TemporaryPassword = password };
    }

    /// <summary>
    /// Compte avec un mot de passe choisi : ligne de commande et administrateur initial.
    /// </summary>
    public async Task<AccountResult> CreateWithPasswordAsync(string? email, string? displayName, string? role, string password, bool mustChangePassword) =>
        await CreateCoreAsync(email, displayName, role, password, mustChangePassword);

    public async Task<AccountResult> UpdateAsync(
        Guid actorId,
        Guid id,
        string? displayName,
        string? role,
        IReadOnlyCollection<Guid> formIds,
        CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return AccountResult.Fail("Compte introuvable.");
        }

        var name = displayName?.Trim() ?? string.Empty;
        if (ValidateName(name) is { } nameError)
        {
            return AccountResult.Fail(nameError);
        }

        if (role is null || !AppRoles.All.Contains(role))
        {
            return AccountResult.Fail("Rôle inconnu : administrateur, éditeur ou lecteur.");
        }

        var currentRoles = await users.GetRolesAsync(user);
        var wasAdmin = currentRoles.Contains(AppRoles.Admin);
        var roleChanged = !currentRoles.SequenceEqual([role]);
        if (roleChanged && wasAdmin)
        {
            if (actorId == id)
            {
                return AccountResult.Fail(SelfChangeRefused);
            }

            if (user.DisabledAt is null && !await OtherActiveAdminExistsAsync(id, cancellationToken))
            {
                return AccountResult.Fail(LastAdminRefused);
            }
        }

        user.DisplayName = name;
        await users.UpdateAsync(user);
        if (roleChanged)
        {
            await users.RemoveFromRolesAsync(user, currentRoles);
            await users.AddToRoleAsync(user, role);
            // Le rôle est inscrit dans le cookie : un nouveau jeton fait recharger la session (5 minutes au plus).
            await users.UpdateSecurityStampAsync(user);
        }

        await SetFormsAsync(id, role == AppRoles.Admin ? [] : formIds, cancellationToken);
        await audit.RecordAsync(AuditActions.UserUpdated, actorId, $"user:{id}", $"role:{role}", cancellationToken);
        return new AccountResult([], id);
    }

    public async Task<AccountResult> SetDisabledAsync(Guid actorId, Guid id, bool disabled, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return AccountResult.Fail("Compte introuvable.");
        }

        if (disabled == (user.DisabledAt is not null))
        {
            return new AccountResult([], id);
        }

        if (disabled)
        {
            if (actorId == id)
            {
                return AccountResult.Fail(SelfChangeRefused);
            }

            if (await users.IsInRoleAsync(user, AppRoles.Admin) && !await OtherActiveAdminExistsAsync(id, cancellationToken))
            {
                return AccountResult.Fail(LastAdminRefused);
            }
        }

        user.DisabledAt = disabled ? timeProvider.GetUtcNow() : null;
        await users.UpdateAsync(user);
        // Nouveau jeton de sécurité : la session ouverte du compte désactivé tombe (5 minutes au plus).
        await users.UpdateSecurityStampAsync(user);
        await audit.RecordAsync(disabled ? AuditActions.UserDisabled : AuditActions.UserEnabled, actorId, $"user:{id}", cancellationToken);
        return new AccountResult([], id);
    }

    /// <summary>Remplace le mot de passe par un mot de passe provisoire et coupe les sessions ouvertes.</summary>
    public async Task<AccountResult> ResetPasswordAsync(Guid? actorId, Guid id, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return AccountResult.Fail("Compte introuvable.");
        }

        var password = GenerateTemporaryPassword();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
        {
            return new AccountResult(reset.Errors.Select(e => e.Description).ToList());
        }

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        // Un compte bloqué après trop d'essais redevient utilisable avec le nouveau mot de passe.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await audit.RecordAsync(AuditActions.UserPasswordReset, actorId, $"user:{id}", cancellationToken);
        return new AccountResult([], id, password);
    }

    /// <summary>Téléphone perdu sans code de secours : connexion par mot de passe seul jusqu'à la réactivation.</summary>
    public async Task<AccountResult> ResetTwoFactorAsync(Guid? actorId, Guid id, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return AccountResult.Fail("Compte introuvable.");
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await audit.RecordAsync(AuditActions.TwoFactorReset, actorId, $"user:{id}", cancellationToken);
        return new AccountResult([], id);
    }

    /// <summary>
    /// Administrateur initial, créé une seule fois quand la base ne contient aucun compte.
    /// Renvoie <c>false</c> sans rien faire si un compte existe déjà.
    /// </summary>
    public async Task<bool> BootstrapAdminAsync(string email, string? displayName, string password, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        var result = await CreateCoreAsync(email, string.IsNullOrWhiteSpace(displayName) ? "Administrateur" : displayName, AppRoles.Admin, password, mustChangePassword: true);
        if (!result.Succeeded)
        {
            // Mot de passe trop court, adresse invalide... : jamais le mot de passe dans le message.
            throw new InvalidOperationException("Administrateur initial refusé : " + string.Join(" ", result.Errors));
        }

        await audit.RecordAsync(AuditActions.BootstrapAdminCreated, null, $"user:{result.UserId}", cancellationToken);
        return true;
    }

    private async Task<AccountResult> CreateCoreAsync(string? email, string? displayName, string? role, string password, bool mustChangePassword)
    {
        var address = email?.Trim() ?? string.Empty;
        var name = displayName?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            return AccountResult.Fail("L'adresse email est obligatoire.");
        }

        if (ValidateName(name) is { } nameError)
        {
            return AccountResult.Fail(nameError);
        }

        if (role is null || !AppRoles.All.Contains(role))
        {
            return AccountResult.Fail("Rôle inconnu : administrateur, éditeur ou lecteur.");
        }

        await EnsureRolesAsync();
        var user = new AppUser
        {
            Email = address,
            UserName = address,
            EmailConfirmed = true,
            DisplayName = name,
            CreatedAt = timeProvider.GetUtcNow(),
            MustChangePassword = mustChangePassword,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return new AccountResult(created.Errors.Select(e => e.Description).Distinct().ToList());
        }

        await users.AddToRoleAsync(user, role);
        return new AccountResult([], user.Id);
    }

    private static string? ValidateName(string name) =>
        name.Length == 0 ? "Le nom affiché est obligatoire."
        : name.Length > AppUser.DisplayNameMaxLength ? $"Le nom affiché ne doit pas dépasser {AppUser.DisplayNameMaxLength} caractères."
        : null;

    private async Task<bool> OtherActiveAdminExistsAsync(Guid exceptId, CancellationToken cancellationToken)
    {
        var adminRoleId = await db.Roles.Where(r => r.Name == AppRoles.Admin).Select(r => r.Id).SingleAsync(cancellationToken);
        return await db.UserRoles
            .Where(ur => ur.RoleId == adminRoleId && ur.UserId != exceptId)
            .Join(db.Users, ur => ur.UserId, u => u.Id, (_, u) => u)
            .AnyAsync(u => u.DisabledAt == null, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> RolesByUserAsync(CancellationToken cancellationToken) =>
        (await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken))
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Order(StringComparer.Ordinal)));

    private async Task SetFormsAsync(Guid userId, IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken)
    {
        var wanted = await db.Forms.Where(f => formIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(cancellationToken);
        var existing = await db.UserFormAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        db.UserFormAccess.RemoveRange(existing.Where(a => !wanted.Contains(a.FormId)));
        db.UserFormAccess.AddRange(wanted
            .Where(id => existing.All(a => a.FormId != id))
            .Select(id => new UserFormAccess { UserId = userId, FormId = id }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
