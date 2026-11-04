# CLAUDE.md

Instructions de travail pour Claude Code sur ce dépôt.

## Projet

Outil de formulaires du CSE : formulaires publics accessibles par QR code, back-office réservé aux élus, génération de PDF, notifications sans donnée personnelle. Premier cas d'usage : la fiche d'inscription au CSE. Le socle doit rester générique pour créer d'autres formulaires sans développement.

La référence fonctionnelle est `specs.md`. En cas de doute ou de contradiction entre une demande et `specs.md`, poser la question avant de coder.

## Stack

- API et back-office : ASP.NET Core (.NET 10), C#, Identity, TOTP optionnel
- Front public : rendu piloté par la définition JSON du formulaire
- Base de données : PostgreSQL (réponses en JSONB), migrations EF Core
- File de tâches : Hangfire (stockage PostgreSQL)
- PDF : Gotenberg (HTML vers PDF), conteneur séparé
- Email : Mail API existante (MailKit)
- QR code : QRCoder
- Anti-robot : ALTCHA auto-hébergé + code d'accès + limitation de débit
- Déploiement : Docker Compose derrière Traefik ; CI GitHub Actions avec analyse Trivy

## Règles Git

- Toujours travailler sur une branche dédiée. Ne jamais commiter ni pousser directement sur `main`, `master` ou `staging`.
- Noms de branches en minuscules avec tirets, explicites : `feature/etape-N-sujet`, `feature/sujet`, `fix/sujet`, `docs/sujet`. Jamais de nom généré sans rapport avec le contenu. Une branche fusionnée n'est jamais réutilisée.
- Aucun commit ni push sans ma validation explicite à chaque étape.
- Aucun identifiant de modèle d'IA dans les commits, les PR ou les fichiers du dépôt.
- Messages de commit en français, à l'impératif, courts et précis.

## Méthode de travail

- Avancer par étapes courtes : 5 à 6 fonctionnalités maximum par étape.
- Regrouper le travail qui touche le même code et ordonner les étapes selon leurs dépendances.
- Annoncer le plan de l'étape avant de coder, puis résumer ce qui a été fait et ce qui reste à valider.
- Suivre l'ordre des lots de `specs.md` (section 6.4) ; ne pas anticiper les lots V2 ou V3 sans demande.
- Écrire les tests en même temps que le code (xUnit) ; ne pas déclarer une étape terminée si les tests échouent.

## Architecture du code

- Couches : `Api`, `Application`, `Domain`, `Infrastructure`, `Worker`.
- Le formulaire d'inscription n'est jamais codé en dur : c'est une définition JSON chargée par le socle.
- Toute réponse référence la `form_version` utilisée. Publier une modification crée une nouvelle version.
- La validation serveur fait foi et réutilise le même schéma que le client. Un champ masqué par une condition n'est ni validé ni enregistré.
- La soumission enregistre la réponse puis rend la main ; PDF et emails passent toujours par la file de tâches.
- Les paramètres (sites, bureau du CSE, SMTP, identité visuelle) viennent de la base, jamais du code ou des modèles.

## Sécurité

- Valider et normaliser toutes les entrées côté serveur ; requêtes paramétrées uniquement.
- Gestion d'erreurs explicite ; aucun détail technique ni donnée personnelle dans les messages renvoyés au client.
- Aucun secret en dur : variables d'environnement ou secrets Docker ; `.env` exclu du dépôt, `.env.example` sans valeurs réelles.
- Mots de passe et codes d'accès hachés ; cookies `Secure`, `HttpOnly`, `SameSite=Strict` ; protection CSRF sur le back-office.
- En-têtes de sécurité (HSTS, CSP stricte, X-Content-Type-Options) ; aucune ressource chargée depuis un service tiers.
- PDF stockés hors de la racine web, servis uniquement après authentification et contrôle des droits sur le formulaire.
- Signaler avant de les détailler les changements à risque : droits élevés, exposition réseau, suppression de données, modification de la purge ou des contrôles d'accès.

## Protection des données (RGPD)

- Aucune donnée personnelle dans les emails : notification générique (numéro, formulaire, site, date) et lien vers le back-office. Jamais de PDF en pièce jointe.
- L'accusé de réception au salarié ne contient aucune donnée saisie.
- Aucune donnée personnelle dans les logs applicatifs ; identifier une réponse par son id ou son numéro de dossier.
- Adresses IP utilisées uniquement pour la limitation de débit, jamais stockées avec la réponse.
- Journaliser dans `audit_log` : connexion, consultation, téléchargement, export, suppression, changement de statut.
- Respecter `purge_at` : la purge supprime la réponse et ses fichiers.
- Données de test : uniquement des données fictives. Ne jamais utiliser ni versionner de vraie fiche d'inscription.

## Conventions

- Code et noms techniques en anglais ; libellés d'interface, messages et documentation en français.
- Pas de tiret cadratin dans les textes et la documentation.
- Dates affichées au format JJ/MM/AAAA, stockées en UTC.
- Mettre à jour `specs.md` quand une décision change le comportement attendu, après validation.
