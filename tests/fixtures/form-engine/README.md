# Jeux de tests partagés du moteur de formulaires

Chaque fichier décrit une définition, un contexte et des cas de saisie avec le résultat attendu. Le moteur serveur (C#, `CseForms.Application.Tests`) et le moteur client (`src/CseForms.Api/wwwroot/assets/js/engine.mjs`, `tests/js`) doivent passer exactement les mêmes cas : c'est ce qui garantit que la validation client et la validation serveur ne divergent pas.

Format :

```json
{
  "description": "...",
  "today": "AAAA-MM-JJ",
  "sites": ["..."],
  "definition": { "sections": [] },
  "cases": [
    { "name": "...", "input": {}, "errors": [{ "path": "...", "code": "..." }], "data": {}, "ending": "..." }
  ]
}
```

- `errors` : liste attendue (chemin et code), sans ordre imposé. Vide si la saisie est valide.
- `data` : données normalisées attendues, uniquement si la saisie est valide.
- `ending` : page de fin atteinte par un saut, `null` pour la confirmation standard ; facultatif (non vérifié s'il est absent).

Données exclusivement fictives.
