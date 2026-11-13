# Notice d'information : fiche d'inscription au CSE

Version : projet du 13/11/2026, à valider par le CSE ([specs § 5.4](../../specs.md#54-décisions-à-soumettre-au-cse)).
Référence : articles 13 et 14 du RGPD.

Les passages `[À COMPLÉTER : …]` dépendent d'une décision ou d'une information que le dépôt ne contient pas. Ils doivent tous être levés avant l'ouverture du formulaire.

---

## Texte de la notice complète

> Cette partie est destinée au salarié. Elle est rédigée pour être publiée telle quelle (page liée depuis le formulaire, voir § « Points ouverts »).

### Qui traite vos données ?

Le **Comité social et économique (CSE) NSE**, rue de l'Ambène, 63200 Riom, est responsable du traitement.

Contact pour toute question sur vos données : [À COMPLÉTER : adresse email dédiée du CSE, par ex. une boîte « rgpd » ou la boîte du secrétariat], ou par courrier à l'adresse ci-dessus.

Le CSE n'a pas désigné de délégué à la protection des données ; [À COMPLÉTER : nom ou fonction du référent RGPD désigné par le CSE, par ex. « le secrétaire du CSE »] est votre interlocuteur.

### Pourquoi ces données ?

Pour vous inscrire auprès du CSE et vous donner accès aux **activités sociales et culturelles** (ASC) : prestations, billetterie, chèques, arbre de Noël, etc.

Concrètement, votre fiche permet au CSE :

- de vérifier que vous faites partie des bénéficiaires (contrat, date d'embauche, date de fin le cas échéant, lieu de travail) ;
- d'enregistrer votre foyer pour les prestations qui en dépendent (conjoint, enfants) ;
- de créer votre accès au site du CSE et de vous envoyer un accusé de réception (email personnel).

Vos données ne servent à rien d'autre. Elles ne font l'objet d'aucune décision automatisée ni d'aucun profilage.

### Sur quelle base ?

[À VALIDER : base légale retenue par le CSE, voir le registre.] Proposition : le traitement est nécessaire à la fourniture des prestations ASC que vous demandez au CSE (article 6.1.b du RGPD).

Les champs marqués comme obligatoires sont nécessaires à votre inscription : sans eux, le CSE ne peut pas l'enregistrer. Les sections « Conjoint » et « Enfants » sont facultatives ; sans elles, vous n'aurez pas accès aux prestations qui dépendent de la composition du foyer.

### Quelles données ?

| Catégorie | Données |
| --- | --- |
| Identité | Civilité, nom, prénom |
| Situation professionnelle | Type de contrat, date d'embauche, date de fin de contrat (hors CDI), lieu de travail |
| Coordonnées | Adresse postale, email personnel |
| Conjoint (facultatif) | Nom, prénom |
| Enfants (facultatif) | Nom, prénom, date de naissance, sexe |
| Validation | Accusé de lecture de la présente notice, attestation d'exactitude, date et heure d'envoi, numéro de dossier |

Si vous renseignez les données de votre conjoint ou de vos enfants, merci de les informer de cette transmission et de la présente notice.

Votre adresse IP est utilisée quelques minutes, uniquement pour protéger le formulaire contre les envois automatisés. Elle n'est jamais enregistrée avec votre fiche.

Le formulaire ne dépose aucun cookie de suivi et ne charge aucun service extérieur (pas de mesure d'audience, pas de publicité).

### Qui peut les consulter ?

- Les **seuls membres du CSE habilités** par le CSE à traiter les inscriptions ([À COMPLÉTER : fonctions concernées, par ex. « le secrétaire, le trésorier et leurs adjoints »]). Chaque consultation et chaque téléchargement sont enregistrés.
- Les prestataires techniques du CSE, qui agissent sur ses instructions et sont tenus à la confidentialité :
  - l'hébergeur de l'outil de formulaires, [À COMPLÉTER : nom], en [À COMPLÉTER : France / pays de l'Union européenne] ;
  - l'éditeur du logiciel de gestion du CSE, dans lequel votre inscription est ensuite saisie, [À COMPLÉTER : nom].

**Votre employeur n'a pas accès à ces données.** Le service RH remet le flyer d'inscription mais ne reçoit aucune réponse. Aucune donnée n'est vendue ni transmise à des partenaires commerciaux.

Vos données restent en France ou dans l'Union européenne.

### Combien de temps ?

- **Fiche en ligne et son PDF** : supprimés automatiquement **180 jours** après leur traitement par le CSE (saisie dans le logiciel du CSE). Une fiche pas encore traitée est conservée jusqu'à son traitement.
- **Sauvegardes** : une fiche supprimée disparaît des sauvegardes au plus tard [À COMPLÉTER : 30 jours, durée de rotation des sauvegardes à valider] après sa suppression.
- **Données saisies dans le logiciel du CSE** : [À COMPLÉTER : durée fixée par le CSE pour ses bénéficiaires, par ex. « tant que vous êtes salarié de l'entreprise, puis [N] mois après votre départ »].
- **Numéro de dossier** : il est conservé seul, sans aucune donnée vous concernant, dans le journal de sécurité de l'outil, pour prouver qu'une fiche a été reçue puis supprimée.

### Vos droits

Vous pouvez à tout moment demander au CSE :

- l'**accès** à vos données et leur copie ;
- leur **rectification** : signalez au CSE tout changement d'état civil ou de situation ;
- leur **effacement** ;
- la **limitation** du traitement ;
- la **portabilité** des données que vous avez fournies.

Pour exercer vos droits, écrivez à [À COMPLÉTER : contact ci-dessus] en indiquant si possible votre numéro de dossier. Le CSE peut vous demander de justifier de votre identité en cas de doute. Il vous répond dans un délai d'un mois.

Si vous estimez que vos droits ne sont pas respectés, vous pouvez saisir la **CNIL** (www.cnil.fr, 3 place de Fontenoy, TSA 80715, 75334 Paris Cedex 07).

### Sécurité

Accès réservé aux élus habilités par compte nominatif, double authentification proposée, connexion chiffrée (HTTPS), PDF chiffrés sur le serveur, aucune donnée personnelle dans les emails, journal des consultations, suppression automatique.

---

## Mention courte affichée dans le formulaire

Texte du champ `rgpdNotice` de [`forms/inscription.json`](../../forms/inscription.json). Il ne renvoie pas encore à la notice complète, faute de page publique pour la publier (voir « Points ouverts »). Il reprend la durée de 180 jours de `retentionDays` : les deux valeurs doivent changer ensemble.

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
| Publication de la notice complète | Développement | Le formulaire n'a pas encore de lien vers une notice complète (specs § 4 : « Lien vers la notice complète ») |
