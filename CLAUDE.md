# CLAUDE.md

Outil de formulaires du CSE (.NET 10, PostgreSQL, Hangfire, Gotenberg). Référence fonctionnelle : `specs.md` ; en cas de doute ou de contradiction, demander avant de coder.

## Git (impératif)

- Branche dédiée `feature/…`, `fix/…`, `docs/…` (minuscules, tirets, nom explicite) ; jamais de commit sur `main`/`master`/`staging` ; branche fusionnée jamais réutilisée.
- Aucun commit ni push sans ma validation explicite.
- Commits en français, à l'impératif, courts. Aucun identifiant de modèle d'IA dans le dépôt, les commits ou les PR.

## Versions (tags)

- Tag annoté `vX.Y.Z` (SemVer) sur un commit de `main` ; son push publie les images sur GHCR (`release.yml`). Jamais créé, déplacé ni supprimé sans ma validation.
- Proposer un tag (nom + justification + commits inclus depuis le dernier tag) quand une PR vient d'être fusionnée sur `main`, CI verte, et qu'elle clôt un ensemble livrable : lot de `specs.md` 6.4, fonctionnalité complète, correctif à déployer. Pas de tag pour la doc seule ou du travail intermédiaire.
- Incrément : `Z` correctif sans changement de comportement ; `Y` fonctionnalité compatible, migration additive, variable facultative ; `X` action de l'exploitant requise (variable obligatoire, migration destructive, compose modifié, commande retirée). Préversion `-rc.N` avant une recette.
- Signaler dans la proposition toute migration ou nouvelle variable `.env`.

## Règles non négociables

- Formulaires = définitions JSON versionnées, jamais codés en dur.
- Aucune donnée personnelle dans les emails ni les logs ; données de test fictives uniquement.
- Aucun secret en dur.
- Signaler avant de détailler les changements à risque : droits élevés, exposition réseau, suppression de données, purge, contrôles d'accès.

## À lire selon la tâche

- Code, architecture, méthode, conventions : `docs/claude/architecture.md`
- Sécurité, RGPD, emails, logs, audit, purge : `docs/claude/securite-rgpd.md`
- Lancer, tester, CI, migrations : `README.md`
- Format des définitions JSON : `docs/format-definition.md`
