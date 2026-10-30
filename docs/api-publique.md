# API publique du formulaire

La page du formulaire est servie à l'adresse `/f/{slug}` : une page unique pour tous les formulaires, qui charge la définition par l'API ci-dessous. Ses scripts et sa feuille de style sont servis depuis `/assets`. La CSP n'autorise que les ressources du même site, sans script ni style en ligne.

Un paramètre d'URL préremplit un champ déclaré `"prefill": "url"` si sa valeur figure dans la liste blanche du champ (options ou sites actifs) : `/f/inscription?site=Riom`.

Accessible sans authentification. Toutes les erreurs ont la forme `{ "code": "...", "message": "..." }`, avec un code stable et un message en français, sans détail technique ni valeur saisie.

## Lire un formulaire

`GET /api/public/forms/{slug}`

| Statut | Cas |
| --- | --- |
| 200 | Formulaire publié ou fermé |
| 404 `notFound` | Inconnu ou en brouillon |

```json
{
  "slug": "inscription",
  "title": "Fiche d'inscription au CSE",
  "version": 3,
  "accepting": true,
  "requiresAccessCode": true,
  "definition": { "sections": [] },
  "ui": null,
  "sites": ["Abrest", "Riom"]
}
```

`sites` liste les libellés des sites actifs, pour les champs de type `site`. `definition`, `ui` et `sites` sont vides quand le formulaire n'accepte pas de réponse (fermé, hors période d'ouverture, nombre maximal atteint). Le haché du code d'accès n'est jamais renvoyé.

## Soumettre une réponse

`POST /api/public/forms/{slug}/submissions` (64 Ko au plus)

```json
{ "version": 3, "accessCode": "4821", "data": { "nom": "Dupont" } }
```

`version` est la version affichée au répondant : la réponse n'est enregistrée que si elle est toujours la version publiée.

| Statut | Code | Cas |
| --- | --- | --- |
| 201 | | Enregistrée : `{ "caseNumber": "INS-2026-0042" }` |
| 400 | `badRequest` | Corps illisible ou incomplet |
| 403 | `accessDenied` | Code d'accès absent ou incorrect |
| 404 | `notFound` | Formulaire inconnu ou en brouillon |
| 409 | `versionChanged` | Le formulaire a été republié : recharger la page |
| 409 | `notAccepting` | Fermé, hors période ou complet |
| 413 | `payloadTooLarge` | Corps trop volumineux |
| 422 | `invalid` | Erreurs de saisie dans `errors` (voir `format-definition.md`) |
| 429 | `tooManyRequests` | Limite de débit atteinte |

## Numéro de dossier

`PRÉFIXE-AAAA-NNNN`. Le préfixe est propre au formulaire (`DOS` par défaut). Le rang est tenu par préfixe et par année civile (fuseau Europe/Paris), attribué de façon atomique et jamais réutilisé, même après purge.

## Protection

- Limitation de débit par IP (configurable, voir `.env.example`), prévue large car les salariés d'un même site partagent souvent la même IP de sortie. L'IP n'est ni stockée ni journalisée.
- `X-Forwarded-For` n'est pris en compte que depuis les réseaux déclarés dans `ReverseProxy__KnownNetworks`.
- Les soumissions d'un même formulaire sont sérialisées par un verrou sur sa ligne : le nombre maximal de réponses est respecté même en cas d'envois simultanés.
