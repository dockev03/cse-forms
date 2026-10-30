# Back-office

Réservé aux élus authentifiés, à l'adresse `/admin`. Rendu côté serveur (Razor Pages), sans JavaScript.

## Accès

| Rôle | Accès |
| --- | --- |
| `admin` | Tous les formulaires et toutes les réponses |
| `editor`, `reader` | Uniquement les formulaires attribués (`users grant`) |

Les droits sont vérifiés côté serveur à chaque requête. Un compte sans droit sur un formulaire reçoit un 404, comme pour une ressource inexistante, et le refus est journalisé.

## Sécurité

- Mot de passe de 12 caractères minimum, sans règle de composition imposée.
- Compte bloqué 15 minutes après 5 échecs ; limitation de débit par IP sur la connexion.
- Message d'échec identique, que le compte existe ou non.
- Cookie de session `__Host-cse-session` : `Secure`, `HttpOnly`, `SameSite=Strict`, non persistant, expiré après 30 minutes d'inactivité.
- Jeton anti-CSRF sur tous les formulaires (connexion, déconnexion) ; déconnexion uniquement en POST.
- Redirection après connexion limitée aux adresses `/admin` : pas de redirection ouverte.
- Pages jamais mises en cache (`no-store`) ni indexées ; CSP stricte, sans script.
- Les clés de protection des cookies doivent être conservées (`DataProtection__KeysPath`) pour que les sessions survivent à un redémarrage.

Avec `SameSite=Strict`, un lien ouvert depuis un webmail arrive sans cookie : l'élu passe par l'écran de connexion, puis revient sur la page demandée.

## Écrans

- `/admin` : formulaires accessibles et nombre de réponses.
- `/admin/formulaires/{id}/reponses` : liste paginée (25 par page) ; recherche par numéro de dossier, filtres statut, site et période, tri par date. Seuls le numéro, la date, le site et le statut sont affichés.
- `/admin/reponses/{id}` : fiche avec les libellés de la version remplie, dates au format JJ/MM/AAAA, groupes répétables en tableau. Lien profond utilisable dans les notifications.

## Journal d'audit (`audit_log`)

| Action | Cible |
| --- | --- |
| `login.success`, `login.failure`, `login.lockout`, `logout` | aucune |
| `submission.view` | `submission:{id}` |
| `access.denied` | `submission:{id}` ou `form:{id}` |

Chaque entrée porte l'id du compte (absent pour un identifiant inconnu) et l'horodatage UTC. Aucune donnée saisie, aucun email ni nom.
