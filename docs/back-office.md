# Back-office

Réservé aux élus authentifiés, à l'adresse `/admin`. Rendu côté serveur (Razor Pages), sans JavaScript.

## Accès

| Rôle | Accès |
| --- | --- |
| `admin` | Tous les formulaires et toutes les réponses |
| `editor`, `reader` | Uniquement les formulaires attribués (`users grant`) |

Les droits sont vérifiés côté serveur à chaque requête. Deux cas de refus :

- **Formulaire non attribué** : 404, comme pour une ressource inexistante (l'existence du formulaire ou de la réponse n'est pas révélée). Le refus est journalisé.
- **Formulaire attribué, rôle insuffisant pour l'action** (un `reader` qui change un statut, exporte ou ouvre la diffusion, un `editor` qui supprime) : redirection vers la page « Accès refusé » (`/admin/acces-refuse`), servie en 403 avec un lien vers `/admin`. Le refus est journalisé (`access.denied`). Cette page ne renvoie jamais vers l'écran de connexion, qui ramènerait un compte connecté vers la page refusée, en boucle.

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
- **Téléphone perdu sans code de secours** : un administrateur lance `users reset-2fa --email <email>` ; l'élu se reconnecte avec son mot de passe seul puis réactive la double authentification. `users list` indique qui l'a activée.
- La clé et les codes de secours sont gérés par ASP.NET Identity et stockés en base (table des jetons d'Identity) : ils ne protègent un compte que tant que la base elle-même n'est pas compromise.

## Écrans

- `/admin` : formulaires accessibles et nombre de réponses.
- `/admin/securite` : double authentification du compte connecté.
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
- Règle de notification modifiable sans publier de version (`notifications set`) ; elle s'applique aux envois suivants.
- Réglages : `smtp.host`, `smtp.port` (587 par défaut), `smtp.security` (`starttls` par défaut, `ssl`, `none`), `smtp.from`, nom affiché `cse.name`, adresse publique `app.publicBaseUrl` pour les liens. Identifiants en variables d'environnement du Worker (`Smtp__Username`, `Smtp__Password`) ; pas d'authentification sans chiffrement.
- Test des réglages : `mail test --to <adresse>` sur le Worker (voir le README), avec sa configuration et ses identifiants.
- 3 essais au plus, comme pour le PDF ; seul le type d'erreur est conservé et journalisé (un message SMTP peut citer une adresse). Un envoi réussi clôt la tâche aussitôt, pour ne pas renvoyer le message.

### Alerte d'échec définitif

Après le 3e échec d'une tâche (PDF, notification ou accusé), un email part vers `alerts.email` : type de tâche, numéro de dossier, cause technique (type d'erreur) et lien vers la réponse. L'alerte est envoyée directement, hors file : si elle échoue, elle est seulement journalisée. Sans `alerts.email`, un avertissement est journalisé.

## Journal d'audit (`audit_log`)

| Action | Cible |
| --- | --- |
| `login.success`, `login.failure`, `login.lockout`, `logout` | aucune |
| `login.2fa.failure` (code faux), `login.2fa.recovery_code` (connexion par code de secours) | aucune |
| `2fa.enable`, `2fa.disable`, `2fa.recovery_codes` | aucune |
| `2fa.reset` (`users reset-2fa`) | `user:{id}`, sans compte |
| `submission.view` | `submission:{id}` |
| `submission.download` | `submission:{id}` |
| `submission.pdf.regenerate` | `submission:{id}` |
| `access.denied` | `submission:{id}` ou `form:{id}` |
| `form.qrcode` | `form:{id}` |
| `form.flyer`, `form.flyer.code_rejected` | `form:{id}` |
| `submission.status` | `submission:{id}`, détail `ancien>nouveau` |
| `submission.delete` | `submission:{id}`, détail : numéro de dossier |
| `submission.purge` | `submission:{id}`, sans compte, détail : numéro de dossier |
| `form.export` | `form:{id}`, détail : nombre de lignes |

Chaque entrée porte l'id du compte (absent pour un identifiant inconnu ou une action automatique), l'horodatage UTC et, au besoin, un détail technique (`detail`). Aucune donnée saisie, aucun email ni nom.
