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

## PDF

Le PDF est produit en tâche de fond, jamais pendant la soumission :

1. La soumission enregistre la réponse et une tâche `job` (type `Pdf`, statut `Pending`) dans la même transaction.
2. Le Worker relève les tâches en attente toutes les `Jobs__PollSeconds` secondes (`FOR UPDATE SKIP LOCKED`) et les confie à Hangfire (schéma `hangfire`).
3. La tâche met en page la réponse avec les libellés de la version remplie, la convertit avec Gotenberg (A4), chiffre le fichier et l'enregistre dans `submission_file`. Un seul PDF par réponse : une régénération remplace le précédent et efface l'ancien fichier.
4. En cas d'échec, 3 essais au plus ; seul le type d'erreur est conservé dans `job.last_error`. L'alerte à l'administrateur après un échec définitif arrive avec les emails.

Mise en page générique : nom et coordonnées du CSE (paramètres), titre du formulaire, numéro de dossier, date et heure de soumission (heure de Paris), mention « Formulaire soumis en ligne », sections, groupes répétables en tableau, composition du bureau, pied de page « Document confidentiel » avec numéro de page. Toutes les valeurs sont encodées et le document ne charge aucune ressource externe. Les modèles HTML propres à un formulaire (`form_version.pdf_template_html`) ne sont pas encore pris en charge.

Stockage : AES-256-GCM, clé `Storage__EncryptionKey` (32 octets en base64), fichiers sous `Storage__Path` hors de la racine web, droits réservés au compte du service. L'identifiant du fichier est authentifié avec le contenu ; l'empreinte SHA-256 du contenu en clair est conservée en base. La perte de la clé rend les PDF illisibles : la sauvegarder à part.

## Journal d'audit (`audit_log`)

| Action | Cible |
| --- | --- |
| `login.success`, `login.failure`, `login.lockout`, `logout` | aucune |
| `submission.view` | `submission:{id}` |
| `submission.download` | `submission:{id}` |
| `submission.pdf.regenerate` | `submission:{id}` |
| `access.denied` | `submission:{id}` ou `form:{id}` |

Chaque entrée porte l'id du compte (absent pour un identifiant inconnu) et l'horodatage UTC. Aucune donnée saisie, aucun email ni nom.
