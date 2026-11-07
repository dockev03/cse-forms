# Architecture et méthode (détail)

Stack : voir `specs.md` 6.2. Couches : `Api`, `Application`, `Domain`, `Infrastructure`, `Worker`.

## Socle formulaires

- Le formulaire d'inscription n'est jamais codé en dur : c'est une définition JSON (`forms/`, format dans `docs/format-definition.md`).
- Toute réponse référence la `form_version` utilisée ; publier une modification crée une nouvelle version.
- La validation serveur fait foi et réutilise le schéma du client. Un champ masqué par une condition n'est ni validé ni enregistré.
- La soumission enregistre la réponse puis rend la main ; PDF et emails passent toujours par la file Hangfire.
- Paramètres (sites, bureau du CSE, SMTP, identité visuelle) en base, jamais dans le code ni les modèles.

## Méthode

- Étapes courtes : 5 à 6 fonctionnalités maximum ; regrouper ce qui touche le même code ; ordonner par dépendances.
- Annoncer le plan avant de coder ; à la fin, résumer le fait et le reste à valider.
- Suivre les lots de `specs.md` 6.4 ; pas de V2/V3 sans demande.
- Tests xUnit écrits avec le code ; étape non terminée si un test échoue.
- Mettre à jour `specs.md` quand une décision change le comportement attendu, après validation.

## Conventions

- Code et noms techniques en anglais ; libellés, messages et documentation en français.
- Pas de tiret cadratin dans les textes.
- Dates affichées JJ/MM/AAAA, stockées en UTC.
