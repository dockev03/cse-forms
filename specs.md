# Spécifications : outil de formulaires du CSE

Version : 30/10/2026
Statut : cahier des charges, à valider par le CSE (voir section 5.4)

## 1. Contexte, objectifs et périmètre

L'outil remplace la fiche papier d'inscription au CSE par un formulaire en ligne accessible par QR code. Il génère un PDF consultable par les seuls élus authentifiés, avec une notification par email. Il doit ensuite permettre de créer d'autres formulaires sans développement.

### Situation actuelle

La fiche d'inscription (version mars 2026) est un PDF à remplir puis à renvoyer en original par courrier au CSE à Riom. Sans original, pas de participation. Le CSE ressaisit ensuite la fiche dans son logiciel et coche la case « Saisi dans le logiciel du CSE ».

### Objectifs

1. Supprimer le circuit papier : saisie en ligne depuis un smartphone en moins de 3 minutes.
2. Diffuser le formulaire via un flyer RH avec QR code, remis à l'embauche.
3. Être notifié par email de chaque inscription, le PDF n'étant téléchargeable qu'après authentification d'un élu habilité ; envoyer un accusé de réception au salarié.
4. Suivre les inscriptions dans un back-office : reçue, saisie dans le logiciel CSE, archivée.
5. Réutiliser le même outil pour d'autres formulaires (sondages, inscriptions à un événement, commandes de billetterie, consultations).

### Périmètre

| Lot | Contenu | Priorité |
| --- | --- | --- |
| V1 | Socle générique (formulaires définis en JSON), formulaire d'inscription, QR code, PDF, notification, back-office minimal | Indispensable |
| V1.1 | Éditeur visuel de formulaires (glisser-déposer), logique conditionnelle dans l'éditeur | Important |
| V2 | Sondages anonymes avec statistiques, événements avec quotas, exports, webhooks | Souhaitable |
| V3 | Contacts, segments, SSO, prise de créneau, multilingue | Si le besoin se confirme |
| Hors périmètre | Enquêtes in-app, SDK mobiles, paiement en ligne, IA | Non prévu |

### Principe directeur

Le formulaire d'inscription ne doit pas être codé en dur : c'est le premier formulaire créé avec le socle. Si le socle sait le produire, il saura produire les suivants.

## 2. Acteurs et parcours cible

| Acteur | Rôle dans l'outil | Compte |
| --- | --- | --- |
| Administrateur CSE | Gère les utilisateurs, les paramètres SMTP, la conservation des données | Oui |
| Éditeur CSE (bureau) | Crée et publie les formulaires, consulte et traite les réponses | Oui |
| Lecteur CSE | Consulte les réponses des formulaires auxquels il a droit | Oui |
| RH | Imprime et remet le flyer avec QR code à l'embauche ; n'accède à aucune réponse | Non |
| Salarié (répondant) | Remplit et soumet le formulaire | Non |

### Parcours inscription

1. L'éditeur publie le formulaire et télécharge son QR code (PNG/SVG) et un modèle de flyer PDF.
2. Les RH remettent le flyer au nouvel embauché.
3. Le salarié scanne le QR code et arrive sur une page mobile, sans création de compte.
4. Il saisit, au besoin, un code d'accès imprimé sur le flyer (protection contre les soumissions extérieures).
5. Il remplit le formulaire, lit la mention d'information RGPD et coche la case d'attestation d'exactitude.
6. À la soumission, le serveur valide les données, enregistre la réponse et affiche une page de confirmation avec un numéro de dossier.
7. Un traitement en arrière-plan génère le PDF à partir d'un modèle fidèle à la fiche papier.
8. Une notification sans donnée personnelle part vers la boîte CSE, avec un lien vers la réponse et son PDF, accessibles uniquement après authentification d'un élu habilité. Le salarié reçoit un accusé de réception sans PDF ni donnée saisie.
9. Dans le back-office, l'éditeur saisit l'inscription dans le logiciel CSE puis passe son statut à « Saisie ».

### Exigence de robustesse

L'envoi d'email ne doit jamais bloquer la soumission : la réponse est enregistrée d'abord, la notification est envoyée par une file de tâches avec relances automatiques en cas d'échec.

## 3. Exigences fonctionnelles du socle

Le socle repose sur une définition de formulaire en JSON versionnée : l'affichage, la validation, le PDF et les notifications en découlent.

### 3.1 Types de champs

| Type | Usage | Lot |
| --- | --- | --- |
| Texte court, texte long | Nom, prénom, commentaire | V1 |
| Email, téléphone | Validation de format | V1 |
| Date | Date d'embauche, date de fin | V1 |
| Choix unique (boutons radio ou liste) | Civilité, type de contrat | V1 |
| Choix multiples (cases) | Centres d'intérêt, options | V1 |
| Site (liste paramétrable) | Lieu de travail, réutilisable dans tous les formulaires | V1 |
| Adresse | Adresse, code postal, ville (suggestion de ville depuis le CP) | V1 |
| Groupe répétable | Enfants : nom, prénom, date de naissance, sexe ; bouton « Ajouter un enfant » | V1 |
| Case de consentement | Attestation, accusé de lecture RGPD | V1 |
| Texte d'information | Titres, consignes, mentions (non saisissable) | V1 |
| Nombre | Quantité de billets | V2 |
| Note, échelle, NPS, satisfaction | Sondages de satisfaction | V2 |
| Matrice (Likert) | Questionnaire conditions de travail | V2 |
| Classement | Prioriser des attentes | V2 |
| Choix d'images | Choisir un cadeau, une destination | V2 |
| Fichier | Justificatif (antivirus, limite de taille, types autorisés) | V2 |
| Prise de créneau | Rendez-vous de permanence | V3 |

### 3.2 Règles et logique

- Champs obligatoires, longueurs min/max, format (regex), bornes de date.
- Affichage conditionnel : un champ ou une section n'apparaît que si une condition est vraie (ex. date de fin si le contrat n'est pas un CDI).
- Validations croisées : date de fin postérieure à la date d'embauche ; date de naissance d'un enfant dans le passé.
- Validation exécutée côté client pour l'ergonomie et côté serveur pour la sécurité, à partir du même schéma. Le serveur fait foi.
- Un champ masqué par une condition n'est ni validé ni enregistré.
- Normalisation à l'enregistrement : nom en majuscules, espaces superflus supprimés, email en minuscules.
- Formulaire en plusieurs étapes optionnel, avec barre de progression.
- Champs cachés préremplis par paramètre d'URL (ex. un QR code par site qui préremplit le lieu de travail), avec liste blanche des valeurs acceptées.
- V1.1 : saut vers une section ou une page de fin, plusieurs pages de fin.
- V2 : variables calculées (total, score, nombre de places) et rappel d'une réponse dans un libellé (`Merci {prénom}`).

### 3.3 Diffusion

- URL publique courte par formulaire (ex. `/f/inscription`).
- Génération du QR code (PNG et SVG haute définition) et d'un flyer PDF à imprimer : titre, QR code, URL lisible, code d'accès, contact CSE.
- Statuts : brouillon, publié, fermé ; dates d'ouverture et de fermeture ; nombre maximal de réponses.
- Contrôle d'accès au choix : public, code d'accès (PIN, 4 à 12 chiffres, stocké haché), lien à usage unique (V2). Un formulaire en mode code d'accès n'est jamais publié sans code.
- Quotas par critère avec liste d'attente (V2).
- Personnalisation : logo CSE, couleur principale, image de fond, texte de confirmation.
- Intégration sur le site du CSE par iframe (V2).
- Mode anonyme garanti pour les sondages (V2) : aucune donnée identifiante, horodatage arrondi au jour.
- Affichage mobile prioritaire, accessible (RGAA : libellés, contrastes, navigation clavier).

### 3.4 Traitement des soumissions

- Enregistrement de la réponse avec la version du formulaire utilisée, l'horodatage et un numéro de dossier lisible (ex. `INS-2026-0042`).
- Génération PDF à partir d'un modèle HTML propre à chaque formulaire, ou d'un modèle générique par défaut (libellé + valeur). Le PDF est stocké chiffré et n'est jamais joint à un email.
- Règles de notification par formulaire : destinataires fixes, objet et corps paramétrables avec variables non personnelles (`{formulaire}`, `{numero}`, `{date}`, `{site}`), lien profond vers la réponse dans le back-office.
- Accusé de réception au répondant (champ email désigné) : numéro de dossier et rappel des modalités, sans aucune donnée saisie.
- File de tâches avec relances (3 essais) et alerte à l'administrateur en cas d'échec définitif (adresse d'alerte dans les paramètres).
- Enregistrement des réponses partielles pour reprise ultérieure (V2).
- Webhook sortant optionnel (V2), sans données personnelles par défaut.

### 3.5 Back-office

- Liste des réponses par formulaire : recherche, filtres (statut, site, période), tri.
- Fiche réponse : valeurs, PDF régénérable, historique des actions.
- Statuts de traitement paramétrables ; pour l'inscription : Nouvelle, Saisie dans le logiciel CSE, Archivée. Ils remplacent la case « Réservé au CSE » de la fiche papier.
- Export CSV ou Excel des réponses filtrées.
- Suppression unitaire et purge automatique selon la durée de conservation du formulaire.
- Duplication d'un formulaire (V1.1), modèles de formulaires (V1.1).
- Étiquettes et notes internes (V2).
- Statistiques (V2) : résumé par question avec graphiques, filtres croisés, taux d'abandon par question.

### 3.6 Administration

- Comptes nominatifs avec rôles (administrateur, éditeur, lecteur), restreints par formulaire si besoin.
- Authentification par mot de passe robuste ; 2FA (TOTP) proposée en option, activable par chaque utilisateur.
- Journal d'audit : connexion, consultation, téléchargement, export, suppression, changement de statut.
- Paramètres : serveur SMTP, liste des sites, composition du bureau, identité visuelle, mentions RGPD par défaut.
- Environnement de test séparé (V1.1).

## 4. Formulaire d'inscription : spécification des champs

| Section | Champ | Type | Obligatoire | Règle |
| --- | --- | --- | --- | --- |
| État civil | Civilité | Choix unique (M., Mme) | Oui | Utilité à valider (section 5) |
| État civil | Nom | Texte court | Oui | Converti en majuscules |
| État civil | Prénom | Texte court | Oui | Première lettre en majuscule |
| Professionnel | Type de contrat | Choix unique (CDI, CDD, apprentissage, stage) | Oui | |
| Professionnel | Date d'embauche | Date | Oui | Pas plus de 6 mois dans le futur |
| Professionnel | Date de fin | Date | Si contrat ≠ CDI | Affichée seulement si contrat ≠ CDI ; postérieure à la date d'embauche |
| Professionnel | Lieu de travail | Site (Abrest, Aulnat, Taverny, Riom, Brive, Varennes, Nizerolles, Soyons) | Oui | Liste gérée dans les paramètres ; préremplissable par l'URL |
| Coordonnées | Adresse | Texte court | Oui | |
| Coordonnées | Code postal | Texte court | Oui | 5 chiffres |
| Coordonnées | Ville | Texte court ou liste | Oui | Suggestion depuis le code postal |
| Coordonnées | Email personnel | Email | Oui | Sert à l'inscription au site CSE ; saisie confirmée (double champ) |
| Conjoint | Nom, prénom | Texte court | Non | Section optionnelle, repliée par défaut |
| Enfants | Nom, prénom, date de naissance, sexe | Groupe répétable | Non | 0 à 10 lignes ; date dans le passé |
| Validation | Attestation d'exactitude | Consentement | Oui | « J'atteste l'exactitude des informations » |
| Validation | Information RGPD | Texte + case de lecture | Oui | Lien vers la notice complète |

- **Rappel à afficher** : « Toute modification d'état civil doit être communiquée au CSE », sur la page de confirmation et dans l'accusé de réception. En V3, un lien personnel de modification pourra le remplacer.
- **PDF généré** : mise en page de la fiche (logo, sections, tableau enfants), numéro de dossier, date et heure de soumission, mention « Formulaire soumis en ligne ». Les coordonnées du bureau viennent des paramètres, jamais du modèle.
- **Champ supprimé** : la case « Réservé au CSE, saisi dans le logiciel » devient un statut du back-office.

### Exemple de définition JSON (extrait)

```json
{
  "slug": "inscription",
  "title": "Fiche d'inscription au CSE",
  "access": { "mode": "pin" },
  "retentionDays": 30,
  "sections": [
    {
      "id": "pro",
      "title": "Informations professionnelles",
      "fields": [
        {
          "id": "contrat",
          "type": "choice",
          "label": "Type de contrat",
          "required": true,
          "options": ["CDI", "CDD", "Apprentissage", "Stage"]
        },
        { "id": "dateEmbauche", "type": "date", "label": "Date d'embauche", "required": true },
        {
          "id": "dateFin",
          "type": "date",
          "label": "Jusqu'au",
          "required": true,
          "visibleIf": { "field": "contrat", "op": "notEquals", "value": "CDI" },
          "rules": [{ "op": "after", "field": "dateEmbauche" }]
        },
        { "id": "site", "type": "site", "label": "Lieu de travail", "required": true, "prefill": "url" }
      ]
    },
    {
      "id": "enfants",
      "title": "Enfants",
      "fields": [
        {
          "id": "enfants",
          "type": "repeat",
          "max": 10,
          "fields": [
            { "id": "nom", "type": "text", "label": "Nom" },
            { "id": "prenom", "type": "text", "label": "Prénom" },
            { "id": "naissance", "type": "date", "label": "Date de naissance", "rules": [{ "op": "past" }] }
          ]
        }
      ]
    }
  ],
  "receipt": { "emailField": "emailPerso", "includeData": false }
}
```

Les destinataires des notifications ne figurent pas dans la définition : ils sont réglés en base par formulaire (table `notification_rule`), pour changer une adresse sans publier de nouvelle version et ne jamais versionner d'adresse dans le dépôt.

## 5. RGPD, sécurité et points à faire valider

Le formulaire collecte des données personnelles sur le salarié, son conjoint et des enfants mineurs. Le CSE en est responsable de traitement.

### 5.1 Exigences RGPD

- **Notice d'information** sur le formulaire : responsable (CSE NSE), finalité (gestion des activités sociales et culturelles), destinataires (membres habilités du CSE), durée de conservation, droits et contact.
- **Minimisation** : chaque champ doit servir une prestation. À justifier : civilité, sexe des enfants, données du conjoint.
- **Durée de conservation** paramétrée par formulaire. Pour l'inscription, proposition : suppression de la réponse et du PDF X jours après le statut « Saisie dans le logiciel CSE ».
- **Registre des traitements** du CSE à compléter.
- **Pas de traceurs** : aucun outil d'analyse d'audience, aucune police ni script chargé depuis un service tiers, donc pas de bandeau cookies.
- **Adresses IP** des répondants : uniquement pour la limitation de débit, purge courte, jamais liées à la réponse.
- **Hébergement** en France ou dans l'Union européenne.

### 5.2 Exigences de sécurité

- HTTPS obligatoire (HSTS), en-têtes de sécurité (CSP stricte, X-Frame-Options sauf pages d'intégration, X-Content-Type-Options).
- Anti-robot sans service tiers : code d'accès du flyer + preuve de travail auto-hébergée (type ALTCHA) + limitation de débit par IP.
- Chiffrement des données au repos (disque ou colonne) et des sauvegardes.
- 2FA optionnelle pour le back-office ; sessions courtes ; journal d'audit.
- PDF stockés hors de la racine web, servis uniquement après authentification et contrôle des droits.
- Protection CSRF sur le back-office, cookies `Secure`, `HttpOnly`, `SameSite=Strict`.
- Hachage des mots de passe et du code d'accès (algorithme adapté, pas de stockage en clair).
- Analyse des dépendances (Trivy) dans la CI GitHub Actions à chaque push ; analyse des images Docker à ajouter avec les Dockerfile.

### 5.3 Notifications et accès aux PDF

Aucune donnée personnelle ne circule par email : le PDF reste sur le serveur et ne se télécharge qu'après authentification d'un élu habilité.

- **Notification au CSE** : objet et corps génériques (« Nouvelle inscription n° INS-2026-0042, site Riom »), sans nom ni coordonnées, et un lien vers la réponse dans le back-office.
- **Clic sur le lien** : si l'élu n'est pas connecté, il passe par l'écran de connexion (identifiant, mot de passe, et code 2FA s'il l'a activée), puis arrive directement sur la réponse. Le lien seul ne donne accès à rien.
- **Habilitation** : seuls les comptes ayant le droit sur ce formulaire voient la réponse ; un refus est journalisé.
- **Téléchargement du PDF** : depuis la fiche réponse, tracé dans le journal d'audit (qui, quand, quel dossier). Option : filigrane « Confidentiel CSE, téléchargé par X le JJ/MM/AAAA ».
- **Accusé au salarié** : numéro de dossier, date, contact CSE, rappel de communiquer tout changement d'état civil ; aucune donnée saisie.
- **Session** : expiration après 30 minutes d'inactivité, déconnexion explicite, appareils de confiance pour 30 jours en option.

### 5.4 Décisions à soumettre au CSE

- [ ] Accepter la fiche dématérialisée à la place de l'original papier, qui conditionne aujourd'hui la participation ; mettre à jour les règles d'accès aux activités sociales et culturelles si elles le prévoient.
- [ ] Valider la liste des champs (minimisation) et la durée de conservation.
- [ ] Acter le principe retenu : notification sans donnée personnelle, PDF accessible aux seuls élus authentifiés et habilités, accusé de réception sans donnée au salarié.
- [ ] Désigner les membres habilités à consulter les réponses.
- [ ] Choisir l'hébergement. Un hébergement indépendant de l'employeur évite toute question sur l'accès de la direction aux données des ASC.
- [ ] Valider le budget (hébergement, nom de domaine) dans le cadre du budget de fonctionnement.
- [ ] Organiser la diffusion du flyer avec les RH, qui ne reçoivent aucune donnée.

## 6. Architecture, données et lots

### 6.1 Architecture cible

```
Salarié (QR code)          Élu CSE (back-office)
        \                         /
         v                       v
   Traefik : HTTPS, limitation de débit
                  |
                  v
   API .NET 10 + front web
   (formulaire public, validation par schéma JSON,
    back-office : comptes, 2FA optionnelle, audit)
        |                 |
        v                 v
   PostgreSQL  <---  Worker (file de tâches)  --->  Stockage PDF chiffré
                     |                 |
                     v                 v
               Gotenberg          Mail API (MailKit)
            (HTML vers PDF)    (notifications, accusés)
```

La réponse est enregistrée d'abord ; le PDF et les emails partent en tâche de fond. Le rendu du formulaire est piloté par sa définition JSON : ajouter un formulaire ne demande aucun redéploiement.

### 6.2 Choix techniques

| Brique | Proposition | Alternative |
| --- | --- | --- |
| API et back-office | ASP.NET Core (.NET 10), Identity + TOTP optionnel | Blazor Server pour tout faire en C# |
| Rendu du formulaire public | React ou Vue léger, piloté par le JSON | Bibliothèque de rendu existante (licence à vérifier) |
| Schéma et validation | JSON Schema + extension maison pour l'affichage conditionnel | Schéma maison simple |
| Base de données | PostgreSQL, réponses en JSONB | |
| File de tâches | Hangfire (stockage PostgreSQL) | Worker .NET + table de tâches |
| PDF | Gotenberg (HTML vers PDF via Chromium) | QuestPDF (licence à vérifier) |
| Email | Mail API existante (MailKit) | SMTP direct |
| QR code | QRCoder (.NET) | |
| Anti-robot | ALTCHA (auto-hébergé) + code d'accès | |
| Déploiement | Docker Compose derrière Traefik, CI GitHub Actions + Trivy | |

### 6.3 Modèle de données

| Table | Champs principaux | Rôle |
| --- | --- | --- |
| `form` | id, slug, titre, statut, version_courante, ouverture, fermeture, code_acces_hash, max_reponses, conservation_jours, prefixe_dossier | Formulaire |
| `form_version` | id, form_id, numero, schema_json, ui_json, pdf_template_html, created_at, created_by | Définition figée à chaque publication |
| `notification_rule` | id, form_id, destinataires, champ_email_accuse, objet, corps, lien_reponse | Envois par formulaire |
| `submission` | id, numero_dossier, form_version_id, data_jsonb, statut, created_at, purge_at | Réponse |
| `submission_file` | id, submission_id, type (pdf, upload), chemin, sha256 | PDF généré et pièces |
| `job` | id, submission_id, type, tentatives, erreur, statut | Suivi PDF et emails |
| `app_user`, `role`, `user_form_access` | comptes, rôles, droits par formulaire | Back-office |
| `case_number_counter` | prefixe, annee, dernier_rang | Numérotation des dossiers (`PRÉFIXE-AAAA-NNNN`), atomique, jamais réutilisée même après purge |
| `audit_log` | id, user_id, action, cible, horodatage | Traçabilité |
| `site` | id, libelle, actif | Liste réutilisable |
| `setting` | cle, valeur | Paramètres (bureau, SMTP, identité visuelle) |

Le compteur de dossiers est tenu par préfixe et par année civile (fuseau Europe/Paris), et non par formulaire : deux formulaires partageant un préfixe ne produisent jamais le même numéro.

Une réponse pointe toujours vers la version du formulaire remplie : modifier un formulaire publié crée une nouvelle version et ne casse ni les anciennes réponses ni leur PDF.

### 6.4 Lots de réalisation

1. **Lot 0, cadrage** : validation des décisions de la section 5.4, choix de l'hébergement.
2. **Lot 1, socle** : modèle de données, rendu d'un formulaire depuis le JSON, validation serveur, enregistrement, back-office liste et fiche.
3. **Lot 2, inscription** : définition JSON du formulaire, modèle PDF fidèle à la fiche, notifications, QR code et flyer.
4. **Lot 3, sécurisation** : 2FA optionnelle, audit, anti-robot, purge automatique, sauvegardes, recette RGPD.
5. **Lot 4, mise en service** : test avec quelques salariés volontaires, impression des flyers, communication.
6. **Lot 5, éditeur visuel** : création de formulaires sans écrire de JSON, duplication, modèles, aperçu.
7. **Lot 6, V2** : sondages anonymes et statistiques, quotas, liens à usage unique, exports avancés, webhooks.

## 7. Catalogue fonctionnel complet

Lots : V1 (inscription), V1.1 (éditeur), V2 (sondages et événements), V3 (plus tard, si le besoin se confirme).

| Domaine | Fonctionnalité | Intérêt pour le CSE | Lot |
| --- | --- | --- | --- |
| Conception | Définition JSON versionnée | Modifier un formulaire sans casser les réponses déjà reçues | V1 |
| Conception | Groupe répétable | Liste des enfants, des participants à une sortie | V1 |
| Conception | Formulaire en plusieurs étapes | Saisie confortable sur smartphone | V1 |
| Conception | Éditeur visuel (glisser-déposer) | Créer un sondage sans développeur | V1.1 |
| Conception | Aperçu avant publication (mobile et ordinateur) | Vérifier le rendu avant diffusion | V1.1 |
| Conception | Modèles de formulaires | Partir d'un modèle : inscription, satisfaction, événement | V1.1 |
| Conception | Duplication | Réutiliser le formulaire de l'arbre de Noël d'une année sur l'autre | V1.1 |
| Conception | Bibliothèque de questions | Réutiliser les blocs standard (identité, site, consentement) | V2 |
| Conception | Formulaire multilingue | Salariés non francophones | V3 |
| Types de questions | Texte, email, téléphone, date, choix, site, adresse, consentement | Couvre la fiche d'inscription | V1 |
| Types de questions | Nombre, liste déroulante longue | Quantités de billets, longues listes | V2 |
| Types de questions | Note (étoiles), échelle, NPS, satisfaction | Évaluer une sortie, une prestation, la permanence | V2 |
| Types de questions | Matrice (Likert) | Questionnaire conditions de travail ou RPS | V2 |
| Types de questions | Classement | Prioriser les attentes des salariés | V2 |
| Types de questions | Choix d'images | Choisir un cadeau ou une destination sur photo | V2 |
| Types de questions | Dépôt de fichier | Justificatif pour une aide ou un remboursement | V2 |
| Types de questions | Prise de créneau | Réserver un créneau de permanence ou de rendez-vous avec un élu | V3 |
| Logique | Affichage conditionnel | Date de fin seulement hors CDI | V1 |
| Logique | Champs cachés préremplis par l'URL | Un QR code par site qui préremplit le lieu de travail | V1 |
| Logique | Saut vers une section ou une fin | Parcours différent selon la situation | V1.1 |
| Logique | Plusieurs pages de fin | Message adapté : inscrit, liste d'attente, non éligible | V1.1 |
| Logique | Variables calculées | Total, score d'un quiz, nombre de places | V2 |
| Logique | Rappel de réponse dans le texte | « Merci {prénom}, combien d'enfants participeront ? » | V2 |
| Diffusion | Lien court et QR code | Flyer RH, affichage sur les panneaux CSE | V1 |
| Diffusion | Flyer PDF généré | Impression immédiate par les RH | V1 |
| Diffusion | Ouverture, fermeture, nombre maximal de réponses | Sortie limitée en places | V1 |
| Diffusion | Code d'accès | Réserver le formulaire aux salariés | V1 |
| Diffusion | Personnalisation visuelle | Logo et couleurs du CSE | V1 |
| Diffusion | Liens à usage unique | Vote ou sondage, une réponse par personne | V2 |
| Diffusion | Quotas par critère | 20 places par site, liste d'attente au-delà | V2 |
| Diffusion | Intégration sur le site du CSE | Formulaires visibles directement sur le portail | V2 |
| Diffusion | Mode anonyme garanti | Sondage sans donnée identifiante | V2 |
| Réponses | Génération PDF par modèle | Archive fidèle à la fiche papier | V1 |
| Réponses | Statuts de traitement | Suivi « Nouvelle, Saisie, Archivée » | V1 |
| Réponses | Enregistrement des réponses partielles | Reprendre plus tard un formulaire long | V2 |
| Réponses | Étiquettes et notes internes | Annoter un dossier entre élus | V2 |
| Réponses | Lien personnel de modification | Le salarié met à jour son état civil lui-même | V3 |
| Analyse | Export CSV et Excel | Import dans le logiciel CSE, annexe d'un PV | V1 |
| Analyse | Résumé par question avec graphiques | Restitution d'un sondage en réunion plénière | V2 |
| Analyse | Filtres croisés | Résultats par site, par type de contrat | V2 |
| Analyse | Taux d'abandon par question | Repérer une question qui bloque | V2 |
| Analyse | Rapport PDF des résultats | Diffusion des résultats aux salariés | V3 |
| Automatisation | Notification sans donnée personnelle | Alerte à chaque inscription | V1 |
| Automatisation | Accusé de réception au répondant | Preuve de dépôt pour le salarié | V1 |
| Automatisation | Webhooks et API | Alimenter un autre outil (billetterie, portail) | V2 |
| Automatisation | Relance des non-répondants | Rappel avant la clôture (suppose une liste de contacts) | V3 |
| Contacts | Liste des salariés inscrits | Cibler un sondage, envoyer des liens à usage unique | V3 |
| Contacts | Segments (site, contrat) | Sondage réservé à un site | V3 |
| Sécurité | Comptes nominatifs, rôles, droits par formulaire | Seuls les élus habilités voient les réponses | V1 |
| Sécurité | Double authentification optionnelle | Protection renforcée pour qui le souhaite | V1 |
| Sécurité | Journal d'audit | Traçabilité des consultations et téléchargements | V1 |
| Sécurité | Anti-robot auto-hébergé et limitation de débit | Formulaire public via QR code | V1 |
| Sécurité | Purge automatique | Respect des durées de conservation | V1 |
| Sécurité | Connexion via compte Microsoft ou Google (SSO) | Moins de mots de passe pour les élus | V3 |
| Administration | Paramètres centralisés (sites, bureau, SMTP) | Changement de bureau sans toucher aux modèles | V1 |
| Administration | Environnement de test séparé | Tester sans polluer les vraies réponses | V1.1 |
| Administration | Interface multilingue | Utile seulement si l'outil est partagé avec d'autres CSE | V3 |

**Écartés** : enquêtes affichées dans une application tierce selon le comportement de l'utilisateur, kits de développement mobiles, analyse automatique des textes par IA. Ils n'apportent rien au CSE et posent des questions RGPD.
