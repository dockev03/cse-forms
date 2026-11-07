# Back-office

Réservé aux élus authentifiés, à l'adresse `/admin`. Rendu côté serveur (Razor Pages), sans JavaScript.

## Accès

| Rôle | Accès |
| --- | --- |
| `admin` | Tous les formulaires et toutes les réponses |
| `editor`, `reader` | Uniquement les formulaires attribués (`users grant`) |

Les droits sont vérifiés côté serveur à chaque requête. Un compte sans droit sur un formulaire reçoit un 404, comme pour une ressource inexistante, et le refus est journalisé.

## Sécurité

- Mot de passe de 12 caractères minimum, sans règle de composition imposée.
- Compte bloqué 15 minutes après 5 échecs ; limitation de débit par IP sur la connexion.
- Message d'échec identique, que le compte existe ou non.
- Cookie de session `__Host-cse-session` : `Secure`, `HttpOnly`, `SameSite=Strict`, non persistant, expiré après 30 minutes d'inactivité.
- Jeton anti-CSRF sur tous les formulaires (connexion, déconnexion) ; déconnexion uniquement en POST.
- Redirection après connexion limitée aux adresses `/admin` : pas de redirection ouverte.
- Pages jamais mises en cache (`no-store`) ni indexées ; CSP stricte, sans script.
- Les clés de protection des cookies doivent être conservées (`DataProtection__KeysPath`) pour que les sessions survivent à un redémarrage.

Avec `SameSite=Strict`, un lien ouvert depuis un webmail arrive sans cookie : l'élu passe par l'écran de connexion, puis revient sur la page demandée.

## Écrans

- `/admin` : formulaires accessibles et nombre de réponses.
- `/admin/formulaires/{id}/reponses` : liste paginée (25 par page) ; recherche par numéro de dossier, filtres statut, site et période, tri par date. Seuls le numéro, la date, le site et le statut sont affichés.
- `/admin/reponses/{id}` : fiche avec les libellés de la version remplie, dates au format JJ/MM/AAAA, groupes répétables en tableau. Lien profond utilisable dans les notifications. Le bloc PDF indique si le PDF est disponible, en cours de génération ou en échec.
- `/admin/reponses/{id}/pdf` : téléchargement du PDF déchiffré, après contrôle des droits, jamais mis en cache.
- Régénération du PDF : bouton de la fiche (POST avec jeton anti-CSRF), réservé aux rôles `admin` et `editor` ; ignorée si une génération est déjà en cours.
- `/admin/formulaires/{id}/diffusion` : [diffusion](#diffusion-qr-code-et-flyer), réservée aux rôles `admin` et `editor` ayant le droit sur le formulaire.

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
| `submission.view` | `submission:{id}` |
| `submission.download` | `submission:{id}` |
| `submission.pdf.regenerate` | `submission:{id}` |
| `access.denied` | `submission:{id}` ou `form:{id}` |
| `form.qrcode` | `form:{id}` |
| `form.flyer`, `form.flyer.code_rejected` | `form:{id}` |

Chaque entrée porte l'id du compte (absent pour un identifiant inconnu) et l'horodatage UTC. Aucune donnée saisie, aucun email ni nom.
