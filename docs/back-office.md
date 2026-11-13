# Back-office

Réservé aux élus authentifiés, à l'adresse `/admin`. Rendu côté serveur (Razor Pages), sans JavaScript.

## Accès

| Rôle | Accès |
| --- | --- |
| `admin` | Tous les formulaires et toutes les réponses |
| `editor`, `reader` | Uniquement les formulaires attribués (écran « Comptes » ou `users grant`) |

Les droits sont vérifiés côté serveur à chaque requête. Deux cas de refus :

- **Formulaire non attribué** : 404, comme pour une ressource inexistante (l'existence du formulaire ou de la réponse n'est pas révélée). Le refus est journalisé.
- **Formulaire attribué, rôle insuffisant pour l'action** (un `reader` qui change un statut, exporte, ouvre la diffusion, les réglages, les notifications ou l'import, un `editor` qui supprime) : redirection vers la page « Accès refusé » (`/admin/acces-refuse`), servie en 403 avec un lien vers `/admin`. Le refus est journalisé (`access.denied`). Cette page ne renvoie jamais vers l'écran de connexion, qui ramènerait un compte connecté vers la page refusée, en boucle.
- **Compte connecté sans rôle du back-office** : même page « Accès refusé » sur tout `/admin`. Le refus est journalisé (`access.denied`, sans cible, détail `no-role` ; l'adresse demandée n'est pas conservée).

## Sécurité

- Mot de passe de 12 caractères minimum, sans règle de composition imposée.
- Compte bloqué 15 minutes après 5 échecs (mot de passe ou code de double authentification) ; limitation de débit par IP sur la connexion.
- Message d'échec identique, que le compte existe ou non.
- Cookie de session `__Host-cse-session` : `Secure`, `HttpOnly`, `SameSite=Strict`, non persistant, expiré après 30 minutes d'inactivité.
- HTTPS uniquement : une requête HTTP vers `/admin` reçoit un 400 explicite, sans redirection (elle bouclerait derrière un proxy non déclaré dans `ReverseProxy__KnownNetworks`).
- Jeton anti-CSRF sur tous les formulaires (connexion, déconnexion) ; déconnexion uniquement en POST.
- Redirection après connexion limitée aux adresses `/admin` : pas de redirection ouverte.
- Pages jamais mises en cache (`no-store`) ni indexées ; CSP stricte, sans script.
- Les clés de protection des cookies doivent être conservées (`DataProtection__KeysPath`) pour que les sessions survivent à un redémarrage.

Avec `SameSite=Strict`, un lien ouvert depuis un webmail arrive sans cookie : l'élu passe par l'écran de connexion, puis revient sur la page demandée.

## Double authentification

Facultative, activée par chaque élu pour son compte depuis « Sécurité du compte » (`/admin/securite`), avec une application d'authentification (TOTP : Microsoft Authenticator, Google Authenticator, FreeOTP...).

- **Activation** : scanner le QR code (ou saisir la clé), puis confirmer avec un code de l'application. 10 codes de secours sont alors affichés une seule fois ; chacun sert une fois, à la place du code de l'application.
- **Connexion** : après le mot de passe, la page `/admin/connexion/code` demande le code de l'application ou un code de secours (tiret et majuscules facultatifs). Le lien d'une notification reste suivi après les deux étapes.
- **Appareil de confiance** : case à cocher à la saisie du code ; plus de code demandé sur ce navigateur pendant 30 jours (cookie `__Host-cse-device`). Pas avec un code de secours.
- **Nouveaux codes de secours, désactivation** : depuis la même page, sur saisie d'un code valide de l'application. La désactivation change la clé et oublie les appareils de confiance.
- **Téléphone perdu sans code de secours** : un administrateur clique « Désactiver la double authentification » sur la fiche du compte (`/admin/comptes/{id}`) ou lance `users reset-2fa --email <email>` ; l'élu se reconnecte avec son mot de passe seul puis réactive la double authentification. `users list` indique qui l'a activée.
- La clé et les codes de secours sont gérés par ASP.NET Identity et stockés en base (table des jetons d'Identity) : ils ne protègent un compte que tant que la base elle-même n'est pas compromise.

## Comptes

Réservé au rôle `admin` ; les autres rôles reçoivent un 404 sur ces adresses.

- **Administrateur initial** : créé au démarrage de l'API si la base ne contient aucun compte, à partir de `BootstrapAdmin__Email`, `BootstrapAdmin__Password` et `BootstrapAdmin__Name` (facultatif). Ignoré dès qu'un compte existe. Le journal indique la création sans l'adresse ni le mot de passe ; une valeur refusée (mot de passe trop court, adresse invalide) arrête le démarrage avec un message explicite.
- **Mot de passe provisoire** : à la création d'un compte et à la réinitialisation, l'application tire un mot de passe de 19 caractères (`abcd-efgh-jkmn-pqrs`, sans caractères ambigus), affiché une seule fois, jamais envoyé par email. Il se transmet de vive voix ou par un canal distinct. Tant qu'il n'est pas changé, toute page du back-office renvoie vers `/admin/mot-de-passe`.
- **Réinitialisation du mot de passe** : coupe les sessions ouvertes du compte (5 minutes au plus) et le débloque s'il a été bloqué après trop d'essais.
- **Désactivation** (départ d'un élu) : connexion refusée avec le message d'échec habituel, session coupée sous 5 minutes ; les droits sont conservés pour une réactivation. Pas de suppression de compte : le journal d'audit garde la trace des actions.
- **Garde-fous** : un administrateur ne peut ni désactiver son propre compte ni changer son propre rôle ; il reste toujours au moins un administrateur actif.
- **Changement de rôle** : pris en compte sous 5 minutes dans les sessions ouvertes. Un administrateur voit tous les formulaires : ses attributions sont ignorées.
- **Secours** : `users reset-password --email <email>` affiche un mot de passe provisoire au terminal (administrateur qui a oublié le sien).

## Paramètres

Écran « Paramètres » (`/admin/parametres`, lien dans l'en-tête), réservé au rôle `admin` ; les autres rôles reçoivent un 404. Mêmes contrôles que la commande `settings` ; un champ vidé supprime le paramètre.

- **CSE et adresses** : nom, adresse postale, email et téléphone de contact, composition du bureau (une ligne par membre), adresse publique (`app.publicBaseUrl`, HTTPS sans chemin, barre finale retirée), adresse d'alerte (`alerts.email`). Un seul enregistrement pour tout le bloc : une valeur refusée n'enregistre rien et la saisie est conservée. Changer l'adresse publique rend inutilisables les QR codes déjà imprimés.
- **Logo** : PNG ou JPEG de 256 Ko au plus, reconnu à son contenu et non à son nom ; un SVG est refusé. Aperçu, remplacement, retrait.
- **Sites** : ajout (libellé unique, majuscules comprises), désactivation et réactivation. Pas de suppression ni de renommage : les réponses gardent le libellé saisi, et un site désactivé n'est plus proposé ni prérempli par ses QR codes.
- Le serveur SMTP reste réglé en ligne de commande (`settings set --key smtp.*`).
- Le journal garde le nom des paramètres modifiés, jamais leur valeur.

## Écrans

- `/admin` : formulaires accessibles et nombre de réponses.
- `/admin/securite` : double authentification du compte connecté.
- `/admin/mot-de-passe` : changement de son mot de passe (mot de passe actuel, nouveau, confirmation).
- `/admin/comptes`, `/admin/comptes/nouveau`, `/admin/comptes/{id}` : [comptes](#comptes), rôle `admin` uniquement.
- `/admin/parametres` : [paramètres](#paramètres) du CSE, logo et sites, rôle `admin` uniquement.
- `/admin/acces-refuse` : page « Accès refusé » (403), cible des refus pour rôle insuffisant.
- `/admin/formulaires/{id}/reponses` : liste paginée (25 par page) ; recherche par numéro de dossier, filtres statut, site et période, tri par date. Seuls le numéro, la date, le site et le statut sont affichés.
- `/admin/reponses/{id}` : fiche avec les libellés de la version remplie, dates au format JJ/MM/AAAA, groupes répétables en tableau. Lien profond utilisable dans les notifications. Le bloc PDF indique si le PDF est disponible, en cours de génération ou en échec.
- `/admin/reponses/{id}/pdf` : téléchargement du PDF déchiffré, après contrôle des droits, jamais mis en cache.
- Régénération du PDF : bouton de la fiche (POST avec jeton anti-CSRF), réservé aux rôles `admin` et `editor` ; ignorée si une génération est déjà en cours.
- [Statut de traitement](#statuts-de-traitement) : liste et bouton de la fiche, rôles `admin` et `editor`.
- Historique : bas de la fiche, visible de tous les rôles ayant le droit sur le formulaire. Réception, changements de statut (avec l'auteur), téléchargements, demandes de régénération, refus d'accès, PDF généré, notification et accusé envoyés ou en échec. Les consultations restent dans le journal d'audit sans être affichées.
- `/admin/formulaires/{id}/export` : [export CSV](#export-csv) des réponses filtrées, rôles `admin` et `editor`.
- `/admin/reponses/{id}/suppression` : [suppression définitive](#suppression-unitaire), rôle `admin` uniquement.
- `/admin/formulaires/{id}/diffusion` : [diffusion](#diffusion-qr-code-et-flyer), réservée aux rôles `admin` et `editor` ayant le droit sur le formulaire.
- `/admin/formulaires/{id}/reglages` : [réglages du formulaire](#réglages-dun-formulaire), réservés aux rôles `admin` et `editor` ayant le droit sur le formulaire.
- `/admin/formulaires/{id}/notifications` : [notifications au CSE](#notifications-au-cse), rôles `admin` et `editor` ayant le droit sur le formulaire.
- `/admin/formulaires/import` : [import d'un fichier de formulaire](#import-dun-formulaire), rôles `admin` (création et nouvelle version) et `editor` (nouvelle version d'un formulaire attribué).

## Réglages d'un formulaire

Écran `/admin/formulaires/{id}/reglages`, lié depuis la liste des formulaires et depuis la diffusion. Chaque action est un POST avec jeton anti-CSRF, journalisé. La définition, elle, ne change que par l'import d'une nouvelle version.

- **État** : adresse publique, version publiée, état effectif (ouvert, fermé, pas encore ouvert, date de fermeture passée, nombre maximal atteint) et nombre de réponses reçues.
- **Fermer** : refuse immédiatement toute nouvelle réponse, quelles que soient les dates ; les réponses reçues sont conservées. **Rouvrir** remet le formulaire en ligne, dans la limite des dates et du nombre maximal. Importer une nouvelle version ne rouvre jamais un formulaire fermé.
- **Ouverture, fermeture** : saisies à l'heure de Paris, stockées en UTC ; champ vide, pas de borne. La fermeture doit suivre l'ouverture. Une heure qui n'existe pas (passage à l'heure d'été) est refusée ; une heure ambiguë (passage à l'heure d'hiver) est lue en heure d'hiver.
- **Nombre maximal de réponses** : entier positif, champ vide pour aucune limite. Un plafond inférieur aux réponses déjà reçues est accepté : le formulaire cesse d'accepter des réponses. Le contrôle à la soumission verrouille la ligne du formulaire : deux soumissions simultanées ne dépassent pas le plafond.
- **Code d'accès** (formulaires en mode `pin` uniquement) : nouveau code saisi deux fois, 4 à 12 chiffres, stocké haché. Le code actuel n'est jamais affiché ; le code saisi n'est ni journalisé ni réaffiché, même en cas d'erreur. Les flyers déjà imprimés portent l'ancien code. Un formulaire public reste public : le mode d'accès fait partie de la définition.

## Import d'un formulaire

Écran `/admin/formulaires/import`, lié depuis la liste des formulaires et depuis les réglages. Il remplace `forms import` pour l'usage courant, avec les mêmes contrôles ([format](format-definition.md#fichier-de-formulaire)) ; la ligne de commande reste disponible.

- **Fichiers** : le fichier de formulaire (JSON) et, s'il déclare un `pdfTemplate`, le modèle PDF (HTML), 256 Ko au plus chacun, en UTF-8. Ils sont lus en mémoire, jamais écrits sur le disque. Le modèle envoyé est utilisé quel que soit le nom déclaré ; un modèle envoyé sans `pdfTemplate` dans le fichier est refusé.
- **Création ou nouvelle version** : le `slug` du fichier désigne le formulaire. S'il n'existe pas, le formulaire est créé et publié en version 1, par un administrateur uniquement. S'il existe, une nouvelle version est publiée si la définition, la présentation ou le modèle PDF ont changé ; sinon rien n'est publié. Un éditeur ne publie que pour un formulaire qui lui est attribué ; sinon, il reçoit le même message que le formulaire existe ou non, et le refus est journalisé.
- **Code d'accès** : à saisir deux fois au premier import d'un formulaire en mode `pin` ; jamais réaffiché ni journalisé. Ensuite, laisser vide conserve le code actuel ; le changer se fait plutôt depuis les réglages.
- **Passage en accès public** : un fichier en mode `public` pour un formulaire protégé par code supprime ce code. L'import demande alors de cocher une confirmation, puis de renvoyer le fichier ; le passage est journalisé (`form.public`).
- **Compte rendu** : version publiée, ou aucun changement ; changement de durée de conservation (elle s'applique aux réponses traitées ensuite, les dates de purge déjà fixées ne bougent pas) ; formulaire resté fermé (l'import ne rouvre jamais).
- La version garde l'auteur de l'import (`user:{id}`, sans email ni nom ; `cli` en ligne de commande).

## Statuts de traitement

| Code | Libellé |
| --- | --- |
| `new` | Nouvelle (à la réception) |
| `entered` | Saisie dans le logiciel CSE |
| `archived` | Archivée |

Statuts communs à tous les formulaires pour l'instant. Un statut inconnu est refusé. Chaque changement est journalisé (`submission.status`, détail `ancien>nouveau`) et visible dans l'historique de la fiche.

## Conservation et purge

La durée de conservation d'un formulaire (`retentionDays`, 180 jours pour l'inscription) court à partir du traitement : le premier passage à un statut autre que « Nouvelle » fixe la date de suppression, affichée sur la fiche. Passer ensuite de « Saisie » à « Archivée » ne la repousse pas ; revenir à « Nouvelle » l'annule. Une réponse jamais traitée n'est jamais purgée.

Le Worker exécute la purge chaque nuit (tâche Hangfire récurrente `purge-reponses`, 3 h 17 heure de Paris, `Purge__Cron` pour changer l'horaire) : réponse, PDF chiffré et tâches associées sont supprimés. La date est revérifiée au moment de supprimer, sous verrou : une réponse revenue à « Nouvelle » entre-temps est conservée. Chaque purge est tracée (`submission.purge`, sans compte, détail : numéro de dossier).

## Suppression unitaire

Suppression unitaire réservée au rôle `admin`, depuis la fiche : l'écran de confirmation demande de ressaisir le numéro de dossier. La réponse, son PDF chiffré et ses tâches sont supprimés définitivement, sans corbeille. Trace `submission.delete` avec le numéro de dossier. Un fichier chiffré qui ne peut pas être effacé est signalé dans les journaux (identifiant de la réponse uniquement) ; sa ligne étant supprimée, il n'est plus servi.

## Export CSV

Lien « Exporter » de la liste, rôles `admin` et `editor` : toutes les réponses correspondant aux filtres en cours (toutes les pages), triées comme la liste. Le fichier, jamais mis en cache, est prévu pour Excel en français : séparateur point-virgule, UTF-8 avec BOM, dates JJ/MM/AAAA, heure de Paris.

- Colonnes fixes : dossier, date de réception, statut, version du formulaire.
- Puis une colonne par champ, avec les libellés de la version la plus récente ; les champs retirés d'anciennes versions sont ajoutés à la fin. Pas de textes d'information ni de champs de confirmation.
- Adresse : voie, code postal et ville en trois colonnes. Groupe répétable : une colonne par sous-champ et par ligne (« Enfants 1 - Prénom »).
- Une valeur commençant par `=`, `+`, `-` ou `@` est préfixée d'une apostrophe, pour qu'un tableur ne l'exécute pas comme formule (un téléphone `+33...` apparaît donc `'+33...`).
- Chaque export est journalisé (`form.export`, détail : nombre de lignes). Le fichier contient des données personnelles : il reste au CSE.

## Diffusion : QR code et flyer

- Lien public `{app.publicBaseUrl}/f/{slug}` ; sans adresse publique, QR code et flyer sont indisponibles.
- QR code en PNG (impression, 20 pixels par module) ou SVG (vectoriel, sans script ni lien), niveau de correction Q.
- QR code par site : si le formulaire a un champ `site` préremplissable (`"prefill": "url"`), le lien porte `?{champ}={site}`. Seuls les sites actifs sont proposés et acceptés, comme sur le formulaire public.
- Flyer PDF A4 : logo et nom du CSE, titre, site éventuel, QR code, adresse lisible, code d'accès, mention de confidentialité, contact du CSE.
- Code d'accès : stocké haché, il ne peut pas être relu. Pour un formulaire protégé, l'élu le saisit au moment de générer le flyer ; il est vérifié contre le haché, imprimé, puis oublié : ni base, ni journal, ni file de tâches, ni réaffichage en cas d'erreur. Un code erroné est journalisé (sans le code).
- Le flyer est produit à la demande par l'API, qui appelle Gotenberg sur le réseau interne (variable `Gotenberg__Url` de l'API) : le code saisi ne peut pas passer par la file de tâches sans être enregistré.

## PDF

Le PDF est produit en tâche de fond, jamais pendant la soumission :

1. La soumission enregistre la réponse et une tâche `job` (type `Pdf`, statut `Pending`) dans la même transaction.
2. Le Worker relève les tâches en attente toutes les `Jobs__PollSeconds` secondes (`FOR UPDATE SKIP LOCKED`) et les confie à Hangfire (schéma `hangfire`).
3. La tâche met en page la réponse avec les libellés de la version remplie, la convertit avec Gotenberg (A4), chiffre le fichier et l'enregistre dans `submission_file`. Un seul PDF par réponse : une régénération remplace le précédent et efface l'ancien fichier.
4. En cas d'échec, 3 essais au plus ; seul le type d'erreur est conservé dans `job.last_error`. Un échec définitif déclenche une [alerte](#alerte-déchec-définitif).

Mise en page : celle de la version remplie si elle a un modèle propre (`form_version.pdf_template_html`, voir [modèle PDF](format-definition.md#modèle-pdf)), sinon le modèle générique : logo, nom et coordonnées du CSE (paramètres), titre du formulaire, numéro de dossier, date et heure de soumission (heure de Paris), mention « Formulaire soumis en ligne », sections, groupes répétables en tableau, composition du bureau. Dans les deux cas, pied de page « Document confidentiel » avec numéro de page. Toutes les valeurs sont encodées et le document ne charge aucune ressource externe. Un modèle propre inapplicable laisse place au modèle générique.

Stockage : AES-256-GCM, clé `Storage__EncryptionKey` (32 octets en base64), fichiers sous `Storage__Path` hors de la racine web, droits réservés au compte du service. L'identifiant du fichier est authentifié avec le contenu ; l'empreinte SHA-256 du contenu en clair est conservée en base. La perte de la clé rend les PDF illisibles : la sauvegarder à part.

## Emails

Envoyés par le Worker (SMTP direct, MailKit), en texte brut, jamais avec une pièce jointe. La soumission enregistre la réponse et ses tâches (`job`) dans la même transaction, puis rend la main.

| Tâche | Destinataires | Contenu |
| --- | --- | --- |
| `Notification` | Règle du formulaire (`notification_rule`) ; aucune règle : rien n'est envoyé | Formulaire, numéro de dossier, date, site, lien vers la fiche de la réponse |
| `Receipt` | Adresse saisie dans le champ désigné par `receipt.emailField` ; créée seulement si elle est renseignée | Formulaire, numéro de dossier, date et heure, texte `receipt.message`, contact du CSE |

- Aucune donnée saisie dans les messages : le nom, les coordonnées et les autres réponses restent dans le back-office. Le lien seul ne donne accès à rien : il passe par la connexion et le contrôle des droits.
- L'accusé de réception répond au contact du CSE (`Reply-To` : `cse.email`).
- Règle de notification modifiable sans publier de version, dans le back-office ([notifications](#notifications-au-cse)) ou par `notifications set` ; elle s'applique aux envois suivants.
- Réglages : `smtp.host`, `smtp.port` (587 par défaut), `smtp.security` (`starttls` par défaut, `ssl`, `none`), `smtp.from`, nom affiché `cse.name`, adresse publique `app.publicBaseUrl` pour les liens. Identifiants en variables d'environnement du Worker (`Smtp__Username`, `Smtp__Password`) ; pas d'authentification sans chiffrement.
- Test des réglages : `mail test --to <adresse>` sur le Worker (voir le README), avec sa configuration et ses identifiants.
- 3 essais au plus, comme pour le PDF ; seul le type d'erreur est conservé et journalisé (un message SMTP peut citer une adresse). Un envoi réussi clôt la tâche aussitôt, pour ne pas renvoyer le message.

### Notifications au CSE

Écran `/admin/formulaires/{id}/notifications`, lié depuis les réglages du formulaire.

- **Destinataires** : une adresse par ligne (virgule et point-virgule acceptés), 1 à 20, mises en minuscules et dédoublonnées. Une boîte du CSE est préférable à des adresses personnelles.
- **Objet et corps** facultatifs, avec les variables `{formulaire}`, `{numero}`, `{date}`, `{site}`, `{lien}` ; une variable inconnue (par exemple `{nom}`) est refusée, ce qui empêche d'y mettre une donnée saisie. L'objet tient sur une ligne. Sans `{lien}` dans le corps, le lien vers la réponse est ajouté à la fin. Vides : message par défaut.
- **Aperçu** : la règle enregistrée, rendue par le même code que l'envoi réel, sur un dossier fictif (numéro `PRÉFIXE-AAAA-0042`, site « Site d'exemple »).
- **Désactiver** : supprime la règle ; plus aucun message au CSE pour ce formulaire. L'accusé de réception au répondant ne change pas (il se règle dans le fichier de formulaire).
- Sans `app.publicBaseUrl`, l'écran prévient que chaque notification échouera (lien impossible).
- Le journal garde le nombre de destinataires, jamais les adresses.

### Alerte d'échec définitif

Après le 3e échec d'une tâche (PDF, notification ou accusé), un email part vers `alerts.email` : type de tâche, numéro de dossier, cause technique (type d'erreur) et lien vers la réponse. L'alerte est envoyée directement, hors file : si elle échoue, elle est seulement journalisée. Sans `alerts.email`, un avertissement est journalisé.

## Journal d'audit (`audit_log`)

| Action | Cible |
| --- | --- |
| `login.success`, `login.failure`, `login.lockout`, `logout` | aucune |
| `login.2fa.failure` (code faux), `login.2fa.recovery_code` (connexion par code de secours) | aucune |
| `2fa.enable`, `2fa.disable`, `2fa.recovery_codes` | aucune |
| `2fa.reset` (fiche du compte ou `users reset-2fa`) | `user:{id}` ; sans compte en ligne de commande |
| `password.change` | aucune |
| `user.create`, `user.update` | `user:{id}`, détail : rôle |
| `user.disable`, `user.enable` | `user:{id}` |
| `user.password_reset` | `user:{id}` ; sans compte en ligne de commande |
| `user.bootstrap` (administrateur initial) | `user:{id}`, sans compte |
| `submission.view` | `submission:{id}` |
| `submission.download` | `submission:{id}` |
| `submission.pdf.regenerate` | `submission:{id}` |
| `access.denied` | `submission:{id}` ou `form:{id}` ; aucune pour un compte sans rôle (détail `no-role`) ; détail `form-import` pour un import refusé (sans cible si le formulaire n'existe pas) |
| `form.qrcode` | `form:{id}` |
| `form.flyer`, `form.flyer.code_rejected` | `form:{id}` |
| `submission.status` | `submission:{id}`, détail `ancien>nouveau` |
| `submission.delete` | `submission:{id}`, détail : numéro de dossier |
| `submission.purge` | `submission:{id}`, sans compte, détail : numéro de dossier |
| `form.export` | `form:{id}`, détail : nombre de lignes |
| `form.close`, `form.reopen` | `form:{id}` |
| `form.schedule` | `form:{id}`, détail : `opens=…;closes=…;max=…` (UTC, `-` sans limite) |
| `form.access_code` | `form:{id}`, sans le code |
| `form.import` | `form:{id}`, détail : `create;v1` à la création, `v{n}` pour une nouvelle version ; rien si le fichier n'a pas changé |
| `form.public` | `form:{id}` : formulaire protégé passé en accès public par import |
| `form.notifications` | `form:{id}`, détail : `recipients={nombre}`, sans les adresses |
| `form.notifications.remove` | `form:{id}` |
| `settings.change` | `settings`, détail : noms des paramètres modifiés (`cse.name,cse.logo`...), sans les valeurs |
| `site.add`, `site.disable`, `site.enable` | `site:{id}` |

Chaque entrée porte l'id du compte (absent pour un identifiant inconnu ou une action automatique), l'horodatage UTC et, au besoin, un détail technique (`detail`). Aucune donnée saisie, aucun email ni nom.
