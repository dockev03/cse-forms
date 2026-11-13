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

Les autres propriétés de premier niveau (`slug`, `receipt`...) sont ignorées par le moteur et traitées par d'autres composants (voir [accusé de réception](#accusé-de-réception)). Dans une section, un champ ou une règle, toute propriété inconnue est refusée à la publication.

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

Un formulaire se publie depuis un fichier JSON (exemple : [`forms/inscription.json`](../forms/inscription.json)) depuis le back-office ([import](back-office.md#import-dun-formulaire)) ou avec `forms import` (voir le README). L'[éditeur visuel](back-office.md#éditeur-visuel) produit ce même fichier, contrôlé de la même façon. Le fichier réunit les réglages du formulaire, la présentation et la définition :

| Propriété | Rôle | Versionnée |
| --- | --- | --- |
| `slug` | URL publique `/f/{slug}` (obligatoire) | non |
| `title` | Titre affiché (obligatoire) | oui |
| `caseNumberPrefix` | Préfixe des numéros de dossier, 2 à 6 lettres (`DOS` par défaut) | non |
| `retentionDays` | Conservation des réponses en jours, comptée à partir de leur traitement (premier statut autre que « Nouvelle ») ; absente : pas de purge. Inscription : 180 | non |
| `access` | `{ "mode": "public" }` ou `{ "mode": "pin" }` (obligatoire) | non |
| `ui` | Présentation, stockée dans `ui_json` | oui |
| `pdfTemplate` | Nom du fichier HTML du [modèle PDF](#modèle-pdf), placé dans le même dossier ; absent : modèle générique | oui (`pdf_template_html`) |
| `sections`, `receipt` | Définition, stockée dans `schema_json` | oui |

Toute autre propriété est refusée, y compris `notifications` : les destinataires se règlent en base. Réimporter un fichier dont la définition, la présentation et le modèle PDF n'ont pas changé ne crée pas de version ; sinon une nouvelle version est publiée, sans rouvrir un formulaire fermé. En mode `pin`, le formulaire n'est jamais publié sans code d'accès (4 à 12 chiffres, stocké haché) : il est demandé au premier import.

## Accusé de réception

```json
"receipt": { "emailField": "emailPerso", "message": "Toute modification d'état civil doit être communiquée au CSE." }
```

| Propriété | Rôle |
| --- | --- |
| `emailField` | Champ `email` de premier niveau, enregistré (pas `persist: false`), qui reçoit l'accusé (obligatoire) |
| `message` | Texte fixe ajouté à l'accusé, 1000 caractères au plus (facultatif) |

L'accusé ne contient jamais de donnée saisie : numéro de dossier, formulaire, date et heure, `message`, contact du CSE. Aucune autre propriété n'est acceptée. Sans `receipt`, ou si le répondant n'a pas renseigné le champ, aucun accusé n'est envoyé. `receipt` fait partie de la définition versionnée.

## Modèle PDF

Un formulaire peut fournir sa propre mise en page du PDF (exemple : [`forms/inscription.pdf.html`](../forms/inscription.pdf.html)). C'est un fragment HTML (contenu du `body`, avec un bloc `<style>` éventuel), figé dans la version publiée : une réponse garde toujours la mise en page de la version qu'elle a remplie. Le pied de page (numéro de dossier, pages) est ajouté automatiquement.

Syntaxe :

| Balise | Effet |
| --- | --- |
| `{{ nom }}` | Valeur encodée : un texte saisi n'est jamais interprété comme du HTML |
| `{{#if nom}} ... {{else}} ... {{/if}}` | Bloc affiché si la valeur est présente (`{{else}}` facultatif) |
| `{{#each field.groupe}} ... {{/each}}` | Une fois par ligne d'un groupe répétable ; pas d'imbrication |

Noms disponibles :

| Nom | Valeur |
| --- | --- |
| `field.<id>` | Réponse à un champ, mise en forme comme dans le back-office (dates JJ/MM/AAAA, adresse sur une ligne, Oui/Non) ; vide si le champ était masqué |
| `label.<id>` | Libellé du champ dans la version remplie |
| `item.<id>`, `item.index` | Dans `{{#each}}` : champ de la ligne, numéro de ligne à partir de 1 |
| `form.title`, `form.version` | Titre et numéro de version |
| `case.number`, `submitted.date`, `submitted.time` | Numéro de dossier, date et heure de soumission (heure de Paris) |
| `cse.name`, `cse.address`, `cse.email`, `cse.phone`, `cse.office` | Paramètres du CSE |
| `cse.logo` | Image du logo (paramètre `cse.logo`) ; rien s'il n'est pas défini |

Pour `{{#if}}`, une case non cochée, un groupe sans ligne et un texte vide comptent comme absents.

Le modèle est contrôlé à l'import, contre la définition du même fichier : noms inconnus, blocs mal fermés et balises mal formées sont refusés avec leur numéro de ligne. Le modèle ne peut rien charger ni exécuter : sont refusés les balises `script`, `img`, `link`, `iframe`, `svg`, `meta`, `form` (entre autres), les attributs `on...`, `src`, `href`, les `url(...)` et `@import` en CSS, et `javascript:`. La seule image possible est le logo, fourni par les paramètres. Le document produit porte en plus une CSP `default-src 'none'`.

Si un modèle enregistré ne peut pas être appliqué, le PDF est produit avec le modèle générique et l'incident est journalisé, sans donnée de la réponse.

## Jeux de tests partagés

`tests/fixtures/form-engine` contient des cas (définition, saisie, erreurs et données attendues) exécutés par le moteur serveur (`dotnet test`) et par le moteur client `wwwroot/assets/js/engine.mjs` (`node --test tests/js/*.test.mjs`). Toute évolution d'un moteur doit être reportée dans l'autre, avec un cas partagé.
