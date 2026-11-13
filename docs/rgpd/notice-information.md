# Notice d'information : fiche d'inscription au CSE

Version : projet du 13/11/2026, à valider par le CSE ([specs § 5.4](../../specs.md#54-décisions-à-soumettre-au-cse)).
Référence : articles 13 et 14 du RGPD.

Les passages `[À COMPLÉTER : …]` dépendent d'une décision ou d'une information que le dépôt ne contient pas. Ils doivent tous être levés avant l'ouverture du formulaire.

---

## Texte de la notice complète

Le texte destiné au salarié est dans [`notice-publique.md`](notice-publique.md), seule version à maintenir. Il est rédigé dans le Markdown restreint accepté par l'outil (un test vérifie sa syntaxe) et se publie tel quel :

- back-office, écran « Paramètres », carte « Notice sur les données personnelles » : coller le texte, une fois les `[À COMPLÉTER]` levés, puis enregistrer ;
- ou en ligne de commande : `docker compose exec -T api dotnet CseForms.Api.dll settings set --key rgpd.notice < notice-publique.md`.

La notice apparaît alors sur `/donnees-personnelles`, et chaque formulaire public affiche un lien « Vos données personnelles » en bas de page. Tant que le paramètre est vide, ni la page ni le lien n'existent.

---

## Mention courte affichée dans le formulaire

Texte du champ `rgpdNotice` de [`forms/inscription.json`](../../forms/inscription.json). Il ne cite pas la notice complète : le lien « Vos données personnelles » est en bas de chaque page du formulaire. Il reprend la durée de 180 jours de `retentionDays` : les deux valeurs doivent changer ensemble.

> Le CSE NSE (rue de l'Ambène, 63200 Riom), responsable du traitement, utilise ces informations pour gérer votre accès aux activités sociales et culturelles. Seuls les membres du CSE habilités les consultent ; elles ne sont jamais transmises à l'employeur. Elles sont ensuite saisies dans le logiciel du CSE, puis la fiche en ligne est supprimée 180 jours après ce traitement. Vous pouvez accéder à vos données, les faire rectifier ou effacer en écrivant au CSE à cette adresse, et saisir la CNIL.

---

## Points ouverts

| Point | Pour qui | Remarque |
| --- | --- | --- |
| Contact RGPD du CSE (email) | Bureau | Prévoir une boîte fonctionnelle, pas une adresse personnelle d'élu |
| Référent RGPD | Plénière | Désignation à acter au PV |
| Base légale | Bureau | Voir la discussion dans le registre |
| Élus habilités | Plénière | Décision déjà listée en specs § 5.4 |
| Hébergeur et éditeur du logiciel CSE | Bureau | Vérifier qu'un contrat de sous-traitance (art. 28) existe avec chacun |
| Durée de conservation dans le logiciel CSE | Plénière | Hors outil, mais doit figurer dans la notice |
| Rotation des sauvegardes | Bureau | 30 jours proposés dans [mise-en-service § 9](../mise-en-service.md#9-sauvegarde-et-restauration) |
| Minimisation : civilité, sexe des enfants, conjoint | Plénière | Si un champ est supprimé, retirer la ligne correspondante ci-dessus |
| Publication de la notice complète | Bureau | Page `/donnees-personnelles` disponible : coller le texte de `notice-publique.md` dans les paramètres une fois complété |
