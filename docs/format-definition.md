# Format de définition d'un formulaire

Référence du JSON stocké dans `form_version.schema_json`. Il pilote le rendu client et la validation serveur, qui fait foi. Il reste compatible avec l'exemple de `specs.md` (section 4).

## Structure

```json
{
  "sections": [
    {
      "id": "pro",
      "title": "Informations professionnelles",
      "collapsed": false,
      "visibleIf": { "field": "...", "op": "...", "value": "..." },
      "fields": [ { "id": "contrat", "type": "choice", "label": "Type de contrat", "required": true, "options": ["CDI", "CDD"] } ]
    }
  ]
}
```

Les autres propriétés de premier niveau (`slug`, `notifications`, `receipt`...) sont ignorées par le moteur et traitées par d'autres composants. Dans une section, un champ ou une règle, toute propriété inconnue est refusée à la publication.

Les identifiants (`id`) commencent par une lettre et ne contiennent que lettres, chiffres et `_` (64 caractères maximum). Ils sont uniques dans tout le formulaire, et uniques dans chaque groupe répétable.

## Types de champs

| Type | Valeur enregistrée | Propriétés spécifiques |
| --- | --- | --- |
| `text` | texte sur une ligne | `minLength`, `maxLength` (200 par défaut), `pattern`, `transform` |
| `textarea` | texte multiligne | `minLength`, `maxLength` (4000 par défaut), `pattern`, `transform` |
| `email` | email en minuscules | |
| `phone` | chiffres, `+` initial possible, séparateurs supprimés | |
| `date` | `AAAA-MM-JJ` | `rules` de dates |
| `choice` | une des `options` | `options`, `display` (`radio` ou `select`) |
| `multichoice` | liste d'`options`, dans l'ordre de la définition | `options`, `min`, `max` |
| `site` | libellé d'un site actif (paramètres) | |
| `address` | `{ "street", "postalCode", "city" }`, code postal à 5 chiffres, ville en majuscules | |
| `repeat` | liste de lignes | `fields`, `min`, `max` (100 au plus), `addLabel` |
| `consent` | `true` ou `false` ; obligatoire signifie coché | |
| `info` | rien (texte affiché) | `text` |
| `hidden` | une des `options` (liste blanche) | `options`, `prefill` |

Propriétés communes : `label` (obligatoire sauf `info` et `hidden`), `help`, `placeholder`, `required`, `visibleIf`, `rules`, `prefill` (`url`), `persist` (`false` pour un champ de contrôle validé mais jamais enregistré, comme une confirmation d'email).

`transform` : `none`, `upper`, `lower`, `capitalize` (« jean-pierre » devient « Jean-Pierre »).

## Normalisation

Avant validation : suppression des caractères de contrôle, des espaces en tête et en fin, réduction des espaces multiples (sauf retours à la ligne d'un `textarea`), puis `transform`. Une valeur vide après normalisation est considérée comme absente.

## Affichage conditionnel

```json
{ "field": "contrat", "op": "notEquals", "value": "CDI" }
{ "all": [ { "field": "a", "op": "equals", "value": "oui" }, { "field": "b", "op": "in", "value": ["x", "y"] } ] }
{ "any": [ { "field": "a", "op": "empty" }, { "field": "b", "op": "notEmpty" } ] }
```

Opérateurs : `equals`, `notEquals`, `in`, `notIn`, `empty`, `notEmpty`. Pour un choix multiple, `equals` signifie « contient ». Un champ masqué compte comme vide dans les conditions qui le citent. Dans un groupe répétable, un nom de champ désigne d'abord un champ de la même ligne, puis un champ du formulaire. Les conditions circulaires sont refusées.

Un champ masqué, ou dans une section masquée, n'est ni validé ni enregistré.

## Règles

```json
"rules": [ { "op": "after", "field": "dateEmbauche" }, { "op": "notAfter", "value": "today+6M" } ]
```

| Règle | Sens | Cible |
| --- | --- | --- |
| `after`, `before` | strictement après ou avant | `field` (date) ou `value` |
| `notBefore`, `notAfter` | au plus tôt ou au plus tard | `field` (date) ou `value` |
| `past`, `future` | avant ou après aujourd'hui | aucune |
| `sameAs` | identique à un autre champ du même type | `field` |

Valeurs de date : `AAAA-MM-JJ`, `today`, `today+6M`, `today-18Y`, `today+30D` (jours `D`, mois `M`, années `Y`). Une règle dont le champ cible est masqué, vide ou invalide n'est pas appliquée.

## Erreurs renvoyées

Chaque erreur a un chemin (`dateFin`, `adresse.postalCode`, `enfants[0].naissance`), un code stable (`required`, `invalidType`, `tooShort`, `tooLong`, `invalidFormat`, `invalidEmail`, `invalidPhone`, `invalidDate`, `invalidPostalCode`, `invalidOption`, `duplicateOption`, `tooFewItems`, `tooManyItems`, `mustBeAccepted`, `rule.<op>`) et un message en français qui ne reprend jamais la valeur saisie.

## Présentation (`ui_json`)

Optionnel, stocké dans `form_version.ui_json`, sans effet sur la validation.

| Propriété | Effet |
| --- | --- |
| `multiStep` | `true` : une section visible par étape, avec barre de progression ; « Suivant » vérifie la section courante |
| `confirmationText` | Texte affiché sur la page de confirmation, sous le numéro de dossier |

Côté client, un choix unique de 4 options ou moins s'affiche en boutons radio, au-delà en liste déroulante, sauf si `display` est précisé. Une section `collapsed` est repliée par défaut.

## Fichier de formulaire

Un formulaire se publie depuis un fichier JSON (exemple : [`forms/inscription.json`](../forms/inscription.json)) avec `forms import` (voir le README). Le fichier réunit les réglages du formulaire, la présentation et la définition :

| Propriété | Rôle | Versionnée |
| --- | --- | --- |
| `slug` | URL publique `/f/{slug}` (obligatoire) | non |
| `title` | Titre affiché (obligatoire) | oui |
| `caseNumberPrefix` | Préfixe des numéros de dossier, 2 à 6 lettres (`DOS` par défaut) | non |
| `retentionDays` | Conservation des réponses en jours ; absente : non définie | non |
| `access` | `{ "mode": "public" }` ou `{ "mode": "pin" }` (obligatoire) | non |
| `ui` | Présentation, stockée dans `ui_json` | oui |
| `sections`, `receipt` | Définition, stockée dans `schema_json` | oui |

Toute autre propriété est refusée, y compris `notifications` : les destinataires se règlent en base. Réimporter un fichier dont la définition et la présentation n'ont pas changé ne crée pas de version ; sinon une nouvelle version est publiée, sans rouvrir un formulaire fermé. En mode `pin`, le formulaire n'est jamais publié sans code d'accès (4 à 12 chiffres, stocké haché) : il est demandé au premier import.

## Jeux de tests partagés

`tests/fixtures/form-engine` contient des cas (définition, saisie, erreurs et données attendues) exécutés par le moteur serveur (`dotnet test`) et par le moteur client `wwwroot/assets/js/engine.mjs` (`node --test tests/js/*.test.mjs`). Toute évolution d'un moteur doit être reportée dans l'autre, avec un cas partagé.
