# Sécurité et RGPD : règles de code

Exigences fonctionnelles complètes : `specs.md` section 5.

## Sécurité

- Valider et normaliser toutes les entrées côté serveur ; requêtes paramétrées uniquement.
- Erreurs explicites ; aucun détail technique ni donnée personnelle renvoyé au client.
- Aucun secret en dur : variables d'environnement ou secrets Docker ; `.env` hors dépôt, `.env.example` sans valeur réelle.
- Mots de passe et codes d'accès hachés ; cookies `Secure`, `HttpOnly`, `SameSite=Strict` ; CSRF sur le back-office.
- En-têtes HSTS, CSP stricte, X-Content-Type-Options ; aucune ressource tierce.
- PDF hors racine web, servis après authentification et contrôle des droits sur le formulaire.

## RGPD

- Emails : notification générique (numéro, formulaire, site, date) + lien back-office. Jamais de PDF joint. L'accusé au salarié ne contient aucune donnée saisie.
- Logs : aucune donnée personnelle ; identifier une réponse par id ou numéro de dossier.
- IP : uniquement pour la limitation de débit, jamais stockées avec la réponse.
- `audit_log` : connexion, consultation, téléchargement, export, suppression, changement de statut.
- `purge_at` : la purge supprime la réponse et ses fichiers.
- Données de test fictives uniquement.
