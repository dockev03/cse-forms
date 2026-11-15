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
  "sites": ["Abrest", "Riom"],
  "logo": true,
  "privacyNotice": true
}
```

`sites` liste les libellés des sites actifs, pour les champs de type `site`. `definition`, `ui` et `sites` sont vides quand le formulaire n'accepte pas de réponse (fermé, hors période d'ouverture, nombre maximal atteint). Le haché du code d'accès n'est jamais renvoyé. `logo` et `privacyNotice` indiquent si le logo du CSE (`/logo`) et la notice complète sur les données personnelles (`/donnees-personnelles`) sont définis : la page ne demande ou ne lie que ce qui existe.

## Obtenir un défi anti-robot

`GET /api/public/challenge` (même limitation de débit que la lecture d'un formulaire, jamais en cache)

```json
{
  "algorithm": "SHA-256",
  "challenge": "5b0e…(64 caractères hexadécimaux)",
  "maxNumber": 50000,
  "salt": "1f2e3d4c5b6a79880a1b2c3d?expires=1794560400",
  "signature": "9c1d…(64 caractères hexadécimaux)"
}
```

Preuve de travail auto-hébergée au format ALTCHA (protocole implémenté dans le projet, sans bibliothèque ni service tiers, sans traceur) : trouver l'entier `number` entre 0 et `maxNumber` tel que SHA-256(`salt` + `number`), en hexadécimal minuscule, égale `challenge`. Le navigateur la calcule pendant la saisie (`assets/js/pow.mjs`, Web Crypto) : environ 0,3 s sur un ordinateur, 1 s sur un smartphone modeste pour la difficulté par défaut.

- Le défi est signé (HMAC-SHA256, clé `ProofOfWork__HmacKey` de l'API) et expire au bout de `ProofOfWork__ExpiresMinutes` minutes (20 par défaut), date incluse dans le sel.
- Chaque défi ne sert qu'une fois : il est grillé dès qu'une solution valide est présentée, même si la réponse est ensuite refusée (code d'accès, saisie). Le navigateur en prépare un nouveau pour l'envoi suivant.
- Difficulté : `ProofOfWork__MaxNumber` (50 000 par défaut). Doubler la valeur double le temps de calcul moyen.
- Les défis grillés sont conservés jusqu'à leur expiration (table `proof_of_work_used`, empreinte et date seulement), puis supprimés par la purge nocturne du Worker.

## Soumettre une réponse

`POST /api/public/forms/{slug}/submissions` (64 Ko au plus)

```json
{
  "version": 3,
  "accessCode": "4821",
  "data": { "nom": "Dupont" },
  "proof": { "algorithm": "SHA-256", "challenge": "5b0e…", "number": 18342, "salt": "1f2e…?expires=1794560400", "signature": "9c1d…" }
}
```

`proof` est la solution du défi : obligatoire.

`version` est la version affichée au répondant : la réponse n'est enregistrée que si elle est toujours la version publiée.

| Statut | Code | Cas |
| --- | --- | --- |
| 201 | | Enregistrée : `{ "caseNumber": "INS-2026-0042" }`, avec `"ending": "attente"` si un saut a mené à une [page de fin](format-definition.md#sauts-et-pages-de-fin) |
| 400 | `badRequest` | Corps illisible ou incomplet |
| 403 | `proofInvalid` | Preuve anti-robot absente, fausse, expirée ou déjà utilisée : le navigateur réessaie une fois avec un nouveau défi |
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

- Preuve de travail à chaque envoi (voir [défi anti-robot](#obtenir-un-défi-anti-robot)), vérifiée avant toute lecture du formulaire : un envoi automatisé coûte du calcul, une solution ne se rejoue pas.
- Code d'accès imprimé sur le flyer pour les formulaires en mode `pin`.
- Limitation de débit par IP (configurable, voir `.env.example`), prévue large car les salariés d'un même site partagent souvent la même IP de sortie. L'IP n'est ni stockée ni journalisée.
- `X-Forwarded-For` n'est pris en compte que depuis les réseaux déclarés dans `ReverseProxy__KnownNetworks`.
- Les soumissions d'un même formulaire sont sérialisées par un verrou sur sa ligne : le nombre maximal de réponses est respecté même en cas d'envois simultanés.
