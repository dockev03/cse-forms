# Mise en service et exploitation

Guide d'installation, de configuration et d'exploitation en production de l'outil de formulaires du CSE. Il complète le [`README.md`](../README.md) (développement, tests, CI) et renvoie aux documents de référence pour le détail : [back-office](back-office.md), [API publique](api-publique.md), [format des définitions](format-definition.md), [spécifications](../specs.md).

Toutes les valeurs d'exemple (adresses, noms, domaines) sont fictives.

> **Points à risque signalés dans ce guide** : exposition réseau du port de l'API (§ 3.4), déclaration du reverse proxy de confiance (§ 4.3), clé de chiffrement des PDF dont la perte est irréversible (§ 4.2, § 9), purge automatique des réponses (§ 7.4), restauration qui écrase les données (§ 9.3).

## Sommaire

1. [Vue d'ensemble](#1-vue-densemble)
2. [Prérequis](#2-prérequis)
3. [Installation du serveur](#3-installation-du-serveur)
4. [Configuration (`.env`)](#4-configuration-env)
5. [Premier démarrage](#5-premier-démarrage)
6. [Reverse proxy](#6-reverse-proxy)
7. [Paramétrage fonctionnel](#7-paramétrage-fonctionnel)
8. [Recette avant ouverture](#8-recette-avant-ouverture)
9. [Sauvegarde et restauration](#9-sauvegarde-et-restauration)
10. [Mises à jour](#10-mises-à-jour)
11. [Exploitation courante](#11-exploitation-courante)
12. [Dépannage](#12-dépannage)
13. [Référence des variables et paramètres](#13-référence-des-variables-et-paramètres)
14. [Check-list de mise en service](#14-check-list-de-mise-en-service)

---

## 1. Vue d'ensemble

```
 Salarié (QR code)          Élu (back-office /admin)
          \                         /
           v                       v
   Reverse proxy existant (hors projet) — HTTPS, certificat
                     |  HTTP + X-Forwarded-For / X-Forwarded-Proto
                     v
   ┌──────────── hôte Docker ─────────────────────────────────┐
   │  api :8080  (réseaux frontend + backend)                 │
   │     │                                                    │
   │     ├── postgres   (backend, interne)                    │
   │     ├── gotenberg  (backend, interne)  ← flyer           │
   │  worker (backend + egress) ── SMTP ──► serveur de mail   │
   │     └── PDF, notifications, accusés, purge nocturne      │
   │  migrate (s'exécute puis s'arrête à chaque démarrage)    │
   └──────────────────────────────────────────────────────────┘
```

| Service | Image | Rôle | Réseau |
| --- | --- | --- | --- |
| `postgres` | `postgres:17-alpine` | Base de données (réponses, comptes, audit, file de tâches Hangfire) | `backend` (interne) |
| `gotenberg` | `gotenberg/gotenberg:8.38.1` | Conversion HTML → PDF, sans accès aux ressources externes | `backend` |
| `migrate` | `cse-forms-api` | `db migrate` : met le schéma à jour puis s'arrête | `backend` |
| `api` | `cse-forms-api` (construite) | Formulaire public `/f/{slug}`, API `/api`, back-office `/admin`, commandes d'administration | `frontend` + `backend` |
| `worker` | `cse-forms-worker` (construite) | Tâches de fond : PDF, emails, purge ; commande `mail test` | `backend` + `egress` |

Seul le port HTTP de l'API est publié. Le réseau `backend` est `internal` : PostgreSQL et Gotenberg ne sortent pas et ne sont pas joignables depuis l'extérieur. Seul le Worker sort (réseau `egress`), vers le serveur SMTP.

---

## 2. Prérequis

### Serveur

- Linux 64 bits (Debian 12 / Ubuntu 24.04 ou équivalent), hébergé en France ou dans l'UE (exigence RGPD, [specs § 5.1](../specs.md#51-exigences-rgpd)).
- Docker Engine 24+ et le plugin Compose v2 (`docker compose`, pas `docker-compose`).
- 2 vCPU, 2 Go de RAM minimum (Gotenberg embarque Chromium : prévoir 4 Go si plusieurs conversions simultanées), 20 Go de disque.
- Chiffrement du disque recommandé (exigence « données au repos chiffrées », [specs § 5.2](../specs.md#52-exigences-de-sécurité)) : les PDF sont chiffrés par l'application, la base ne l'est pas.
- `git`, `openssl`.
- Heure système synchronisée (NTP) : indispensable pour la double authentification (codes TOTP) et l'horaire de purge.

### Réseau

| Flux | Source → destination | Port |
| --- | --- | --- |
| Entrant | Reverse proxy → hôte Docker | `API_PORT` (8080 par défaut), TCP |
| Sortant | Worker → serveur SMTP | 587 (STARTTLS) ou 465 (SSL) |
| Sortant (construction) | Hôte → `mcr.microsoft.com`, `api.nuget.org`, Docker Hub, miroirs Debian | 443 |
| Administration | Poste d'admin → hôte | SSH |

### À obtenir avant de commencer

- Nom de domaine public (ex. `formulaires.example.test`) et sa configuration sur le reverse proxy existant, avec certificat TLS.
- Adresse IP du reverse proxy **telle que vue par l'hôte Docker**.
- Serveur SMTP : hôte, port, mode de chiffrement, adresse d'expédition, identifiants éventuels.
- Logo du CSE (PNG ou JPEG, 256 Ko au plus), coordonnées, composition du bureau.
- Les décisions du CSE listées en [specs § 5.4](../specs.md#54-décisions-à-soumettre-au-cse), en particulier la liste des élus habilités et la durée de conservation.

---

## 3. Installation du serveur

### 3.1 Docker

Suivre la documentation officielle de Docker pour la distribution (dépôt `download.docker.com`), puis vérifier :

```bash
docker version
```

```bash
docker compose version
```

Ajouter le compte d'exploitation au groupe `docker` revient à lui donner les droits root sur l'hôte : le réserver aux administrateurs.

### 3.2 Récupération du code

```bash
sudo mkdir -p /opt/cse-forms && sudo chown "$USER" /opt/cse-forms
```

```bash
git clone https://github.com/dockev03/cse-forms.git /opt/cse-forms
```

```bash
cd /opt/cse-forms && git checkout main
```

Le nom du dossier (`cse-forms`) sert de nom de projet Compose : les volumes s'appelleront `cse-forms_postgres-data`, `cse-forms_files` et `cse-forms_keys`. Le garder stable.

### 3.3 Pare-feu de l'hôte

Seul le reverse proxy doit joindre le port de l'API. Docker publie ses ports en contournant `ufw`/`iptables INPUT` : filtrer avec `API_BIND` (§ 4.1) et/ou dans la chaîne `DOCKER-USER`. Exemple, proxy en `192.0.2.10`, interface publique `eth0` :

```bash
sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 ! -s 192.0.2.10 -j DROP
```

Rendre la règle persistante (`iptables-persistent` ou équivalent).

### 3.4 Exposition réseau ⚠️

- **Proxy sur une autre machine** : `API_BIND` = adresse de l'interface de l'hôte joignable par le proxy (jamais `0.0.0.0` sans filtrage au pare-feu).
- **Proxy sur le même hôte, hors Docker** : `API_BIND=127.0.0.1`.
- **Proxy dans un conteneur sur le même hôte** : voir § 6.4.

L'API ne fait que du HTTP : un accès direct, sans le proxy, contourne le TLS. Le back-office le refuse (400), mais le formulaire public répondrait en clair.

---

## 4. Configuration (`.env`)

### 4.1 Création du fichier

```bash
cd /opt/cse-forms && cp .env.production.example .env && chmod 600 .env
```

Le fichier `.env` est exclu du dépôt (`.gitignore`) et du contexte de construction Docker (`.dockerignore`). Il ne doit jamais être versionné, copié dans un ticket ou envoyé par email.

### 4.2 Secrets à générer

| Variable | Commande | Remarque |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | `openssl rand -hex 24` | Ni `;` ni guillemet (il entre dans la chaîne de connexion) : l'hexadécimal convient. |
| `Storage__EncryptionKey` | `openssl rand -base64 32` | ⚠️ Clé AES-256 des PDF. **Sa perte rend tous les PDF illisibles, sans recours.** La conserver hors du serveur (coffre-fort de mots de passe du CSE, deux personnes). Aucune procédure de rotation n'existe : ne pas la changer une fois des PDF produits. |
| `ProofOfWork__HmacKey` | `openssl rand -base64 32` | Signe les défis anti-robot. La changer invalide seulement les défis en cours. |

Générer chaque valeur séparément et la coller dans `.env` à la place de `a-remplacer`.

### 4.3 Reverse proxy de confiance ⚠️

```dotenv
ReverseProxy__KnownNetworks=192.0.2.10/32
```

Adresse du proxy **telle que l'API la voit** (CIDR, plusieurs séparées par des virgules). L'API ne fait confiance aux en-têtes `X-Forwarded-For` et `X-Forwarded-Proto` que depuis ces adresses, et un seul saut (`ForwardLimit = 1`).

- Trop restrictif ou faux : le back-office renvoie 400 (HTTPS non reconnu) et la limitation de débit voit toutes les requêtes venir du proxy (une seule IP : blocage collectif).
- Trop large (ex. `0.0.0.0/0`, `10.0.0.0/8`) : n'importe qui peut usurper son IP et se faire passer pour du HTTPS. Toujours un `/32` (ou le plus petit réseau possible).

Pour trouver la bonne valeur, voir § 12.2.

### 4.4 Port

```dotenv
API_BIND=192.0.2.20
API_PORT=8080
```

### 4.5 SMTP

```dotenv
Smtp__Username=formulaires@example.test
Smtp__Password=<mot de passe>
```

Seuls les identifiants sont en variables (Worker uniquement). Le serveur, le port, le chiffrement et l'expéditeur se règlent en base (§ 7.3). Laisser vides pour un relais sans authentification. L'authentification est refusée sans chiffrement (`smtp.security=none`).

### 4.6 Administrateur initial

```dotenv
BootstrapAdmin__Email=admin-cse@example.test
BootstrapAdmin__Name=Administrateur CSE
BootstrapAdmin__Password=<mot de passe de 12 caractères minimum>
```

Compte `admin` créé au premier démarrage de l'API, **uniquement si la base ne contient aucun compte**. Le mot de passe est provisoire : il est demandé de le changer à la première connexion. Les élus suivants se créent ensuite dans le back-office (§ 7.6). Une fois connecté, vider `BootstrapAdmin__Password` dans `.env` : la valeur n'est plus utilisée, inutile de la laisser sur le serveur.

### 4.7 Valeurs facultatives

Les valeurs par défaut de `docker-compose.yml` conviennent à un CSE. Voir le tableau complet au § 13.1 (limitation de débit, difficulté anti-robot, cadence du Worker, horaire de purge).

### 4.8 Vérification

```bash
docker compose config --quiet && echo OK
```

Une variable obligatoire manquante arrête la commande avec un message `... absent`. Vérifier qu'il ne reste aucun `a-remplacer` :

```bash
grep -n "a-remplacer" .env || echo "Aucune valeur à remplacer"
```

---

## 5. Premier démarrage

```bash
docker compose up -d --build
```

Ordre de démarrage : `postgres` (attend d'être prêt) → `migrate` (crée le schéma puis s'arrête avec le code 0) → `api` et `worker`. La première construction prend plusieurs minutes.

Contrôles :

```bash
docker compose ps -a
```

`migrate` doit être `Exited (0)`, les autres `Up`.

```bash
docker compose logs migrate
```

Attendu : `N migration(s) appliquée(s) : ...` (ou `Base à jour`).

```bash
curl -fsS http://127.0.0.1:8080/health/live && echo
```

```bash
curl -fsS http://127.0.0.1:8080/health/ready && echo
```

Les deux répondent `Healthy`. `/health/ready` vérifie que la base est joignable. Adapter l'adresse à `API_BIND`.

---

## 6. Reverse proxy

Le proxy existant (hors projet) termine le TLS et transmet en HTTP à `http://<hôte>:<API_PORT>`. Exigences :

- Transmettre `Host`, `X-Forwarded-For` (IP du client) et `X-Forwarded-Proto: https`.
- Rediriger HTTP → HTTPS côté proxy.
- Ne pas ajouter d'en-têtes de sécurité contradictoires : l'API pose déjà HSTS (365 jours, sur les réponses HTTPS), CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`.
- Ne pas mettre en cache `/admin` ni `/api` (l'API envoie `Cache-Control: no-store`).
- Taille de requête : les soumissions sont du JSON de petite taille, la limite par défaut des proxys (1 Mo pour nginx) suffit.
- Un seul proxy devant l'API. Avec deux sauts (CDN + proxy), seul le dernier est pris en compte.

### 6.1 Exemple nginx

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name formulaires.example.test;

    ssl_certificate     /etc/ssl/formulaires.example.test/fullchain.pem;
    ssl_certificate_key /etc/ssl/formulaires.example.test/privkey.pem;

    location / {
        proxy_pass http://192.0.2.20:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $remote_addr;
        proxy_set_header X-Forwarded-Proto https;
        proxy_http_version 1.1;
    }
}

server {
    listen 80;
    server_name formulaires.example.test;
    return 301 https://$host$request_uri;
}
```

`X-Forwarded-For $remote_addr` (et non `$proxy_add_x_forwarded_for`) écrase toute valeur envoyée par le client.

### 6.2 Exemple Traefik (labels)

Si le proxy existant est un Traefik sur le même hôte, il transmet déjà `X-Forwarded-*`. Ajouter à `api`, dans un fichier `docker-compose.override.yml` non versionné, le réseau du proxy et les labels de routage ; adapter `ReverseProxy__KnownNetworks` (§ 6.4).

### 6.3 Vérification

```bash
curl -sI https://formulaires.example.test/health/live
```

Attendu : `200`, en-tête `strict-transport-security`. Puis ouvrir `https://formulaires.example.test/admin` : l'écran de connexion doit s'afficher (et non une erreur 400).

### 6.4 Proxy conteneurisé sur le même hôte

L'API voit alors l'adresse du proxy sur le réseau Docker partagé (ex. `172.18.0.5`), qui peut changer au redémarrage. Deux solutions :

- **Recommandée** : réseau externe dédié au proxy avec un sous-réseau fixe, et l'IP du proxy fixée (`ipv4_address`) ; `ReverseProxy__KnownNetworks` = cette IP en `/32`.
- À défaut : le `/32` relevé dans les journaux (§ 12.2), à revérifier après chaque recréation du proxy.

Ne jamais déclarer tout le sous-réseau Docker si d'autres conteneurs y sont branchés.

---

## 7. Paramétrage fonctionnel

Toutes les commandes passent par le conteneur `api` (ou `worker` pour `mail test`). Aucune ne prend de secret en argument : mots de passe et codes d'accès sont demandés au terminal.

Alias pratiques pour la session (à taper dans `/opt/cse-forms`) :

```bash
alias cse='docker compose exec api dotnet CseForms.Api.dll'
```

```bash
alias cse-worker='docker compose exec worker dotnet CseForms.Worker.dll'
```

Les conteneurs ont un système de fichiers en lecture seule : un fichier fourni par l'opérateur (logo, nouveau formulaire, texte multi-lignes) se passe par l'entrée standard (`-T` + `<`) ou par un montage temporaire avec `docker compose run` (exemples ci-dessous).

### 7.1 Identité du CSE

```bash
cse settings set --key cse.name --value "CSE Exemple"
```

```bash
cse settings set --key cse.email --value cse@example.test
```

```bash
cse settings set --key cse.phone --value "01 23 45 67 89"
```

```bash
docker compose exec -T api dotnet CseForms.Api.dll settings set --key cse.address < adresse.txt
```

```bash
docker compose exec -T api dotnet CseForms.Api.dll settings set --key cse.office < bureau.txt
```

Logo (PNG ou JPEG, 256 Ko au plus, pas de SVG), via un montage en lecture seule :

```bash
docker compose run --rm --no-deps -v "$PWD/logo-cse.png:/import/logo.png:ro" api settings logo --file /import/logo.png
```

Adresse publique, utilisée pour les liens des emails, le QR code et le flyer :

```bash
cse settings set --key app.publicBaseUrl --value https://formulaires.example.test
```

```bash
cse settings list
```

### 7.2 Sites

```bash
for s in "Site A" "Site B" "Site C"; do cse sites add --label "$s"; done
```

`sites disable --label <libellé>` retire un site des listes sans toucher aux réponses ; `sites enable` le réactive ; `sites list` les affiche.

### 7.3 Emails

```bash
cse settings set --key smtp.host --value smtp.example.test
```

```bash
cse settings set --key smtp.port --value 587
```

```bash
cse settings set --key smtp.security --value starttls
```

```bash
cse settings set --key smtp.from --value formulaires@example.test
```

```bash
cse settings set --key alerts.email --value admin-cse@example.test
```

`smtp.security` : `starttls` (port 587), `ssl` (port 465), `none` (relais interne sans authentification uniquement). `alerts.email` reçoit les alertes d'échec définitif d'une tâche (PDF, notification, accusé) : à renseigner absolument.

Test depuis le Worker, qui détient les identifiants :

```bash
cse-worker mail test --to admin-cse@example.test
```

En cas d'échec, la commande affiche la cause (serveur injoignable, authentification refusée...) et rend un code d'erreur.

### 7.4 Formulaires

Le formulaire d'inscription du dépôt est intégré à l'image :

```bash
cse forms import --file /app/forms/inscription.json
```

Au premier import d'un formulaire en mode `pin`, le code d'accès (4 à 12 chiffres, imprimé sur le flyer) est demandé au terminal. Pour le changer :

```bash
cse forms access-code --form inscription
```

```bash
cse forms list
```

Formulaire hors dépôt (fichier JSON et son modèle PDF dans le même dossier) :

```bash
docker compose run --rm --no-deps -v "$PWD/mes-formulaires:/import:ro" api forms import --file /import/sortie.json
```

Un nouvel import publie une nouvelle version si la définition ou le modèle PDF a changé ; les réponses déjà reçues restent rattachées à leur version. Format : [`format-definition.md`](format-definition.md).

⚠️ **Purge** : `retentionDays` (180 jours pour l'inscription) fixe la suppression définitive des réponses et de leurs PDF, comptée à partir de leur traitement (passage de « Nouvelle » à un autre statut). Le Worker purge chaque nuit à 3 h 17 (heure de Paris, `Purge__Cron`). Faire valider la durée par le CSE avant l'ouverture ([specs § 5.4](../specs.md#54-décisions-à-soumettre-au-cse)) ; une réponse purgée ne se récupère que depuis une sauvegarde.

### 7.5 Notifications

```bash
cse notifications set --form inscription --to elu1@example.test,elu2@example.test --subject "Nouvelle inscription n° {numero}, site {site}"
```

```bash
cse notifications show --form inscription
```

Variables : `{formulaire}`, `{numero}`, `{date}`, `{site}`, `{lien}` ; 20 destinataires au plus ; `--body-file` pour un corps personnalisé (passer par un montage, comme au § 7.4) ; `notifications remove --form <slug>` coupe les notifications. Aucune donnée saisie n'est envoyée par email ([détail](back-office.md#emails)).

L'accusé de réception au salarié se règle dans le fichier du formulaire (`receipt`).

### 7.6 Comptes des élus

Se connecter à `https://<domaine>/admin` avec l'administrateur initial (§ 4.6) et choisir son mot de passe définitif. Puis, menu « Comptes » (`/admin/comptes`) :

1. « Créer un compte » : adresse email, nom affiché, rôle et, pour un éditeur ou un lecteur, les formulaires attribués.
2. Noter le mot de passe provisoire affiché **une seule fois** et le transmettre à l'élu de vive voix ou par un canal distinct, jamais dans le même email que l'identifiant.
3. À sa première connexion, l'élu choisit son propre mot de passe.

| Rôle | Accès |
| --- | --- |
| `admin` | Tous les formulaires et réponses, gestion des comptes |
| `editor` | Formulaires attribués : réponses, statuts, diffusion (QR code, flyer) |
| `reader` | Formulaires attribués, en lecture |

N'habiliter que les élus désignés par le CSE. Recommander à chacun d'activer la double authentification dans « Sécurité du compte » (`/admin/securite`). Depuis la fiche d'un compte : changer le rôle ou les formulaires, réinitialiser le mot de passe (mot de passe oublié, compte bloqué), désactiver la double authentification (téléphone perdu), désactiver le compte (départ d'un élu).

En secours, la ligne de commande fait la même chose : `cse users create|grant|revoke|reset-password|reset-2fa|list` ; par exemple, si le seul administrateur a oublié son mot de passe :

```bash
cse users reset-password --email admin-cse@example.test
```

### 7.7 Diffusion

Dans le back-office : `/admin/formulaires/{id}/diffusion` (rôles `admin` et `editor`) : QR code PNG/SVG, QR code par site et flyer PDF avec le code d'accès. Prérequis : `app.publicBaseUrl` (§ 7.1). Le flyer est la seule chose remise aux RH, qui ne reçoivent aucune donnée.

---

## 8. Recette avant ouverture

À faire depuis l'extérieur, avec des données fictives, puis supprimer les réponses de test depuis la fiche (suppression unitaire, tracée).

| # | Vérification | Attendu |
| --- | --- | --- |
| 1 | `https://<domaine>/health/ready` | `Healthy` |
| 2 | `http://<domaine>/` | Redirection vers HTTPS |
| 3 | Accès direct `http://<hôte>:8080` depuis un poste qui n'est pas le proxy | Refusé (pare-feu) |
| 4 | Scanner le QR code avec un smartphone en 4G | Formulaire affiché, code d'accès demandé |
| 5 | Soumettre une réponse fictive | Numéro de dossier affiché |
| 6 | Email de notification reçu | Aucun nom ni coordonnée, lien vers la fiche |
| 7 | Accusé de réception reçu (si `receipt`) | Aucune donnée saisie |
| 8 | Lien de la notification | Passe par la connexion, puis ouvre la fiche |
| 9 | PDF de la réponse | Généré, téléchargeable, logo et coordonnées du CSE corrects |
| 10 | Compte `editor` sans droit sur le formulaire | 404 sur la réponse |
| 10 bis | Compte `editor` avec droit, adresse `/admin/reponses/<id>/suppression` | Page « Accès refusé », sans boucle vers la connexion |
| 11 | 5 mauvais mots de passe | Compte bloqué 15 minutes |
| 12 | Double authentification | Activation, connexion avec code, code de secours |
| 13 | Export CSV | Fichier conforme ; ouvrir hors de tout poste partagé |
| 14 | Redémarrage `docker compose restart api` | Les élus connectés le restent (clés `keys` conservées) |
| 15 | Journal d'audit | Connexions, consultations et téléchargements tracés |
| 16 | `https://<domaine>/admin` dans un outil d'analyse d'en-têtes (ex. securityheaders, Mozilla Observatory) | HSTS, CSP, X-Frame-Options présents |

---

## 9. Sauvegarde et restauration

### 9.1 Quoi sauvegarder

| Élément | Où | Fréquence | Remarque |
| --- | --- | --- | --- |
| Base PostgreSQL | volume `postgres-data` | Quotidienne | Réponses, comptes, audit, paramètres |
| PDF chiffrés | volume `files` | Quotidienne, **avec** la base | Inutiles sans la clé |
| `Storage__EncryptionKey` | `.env` | Une fois, **à part** | Coffre-fort, hors serveur, jamais avec les sauvegardes |
| `.env` complet | `/opt/cse-forms/.env` | À chaque changement | Contient tous les secrets : coffre-fort |
| Clés des cookies | volume `keys` | Facultatif | Leur perte déconnecte seulement les élus |

Les sauvegardes contiennent des données personnelles : les chiffrer (ex. `age`, `gpg`, ou un outil comme `restic`/`borg`), les stocker en France/UE, en restreindre l'accès. Leur durée de conservation doit rester cohérente avec la purge (une sauvegarde gardée un an conserve des réponses purgées depuis) : 30 jours glissants est un bon compromis à faire valider.

### 9.2 Script de sauvegarde

Exemple `/opt/cse-forms-backup/sauvegarde.sh` (dossier hors du dépôt, droits 700) :

```bash
#!/usr/bin/env bash
set -euo pipefail
cd /opt/cse-forms
set -a; . ./.env; set +a

dest=/var/backups/cse-forms
stamp=$(date +%Y%m%d-%H%M%S)
mkdir -p "$dest" && chmod 700 "$dest"

# Base, format personnalisé (compressé, restauration sélective possible)
docker compose exec -T postgres pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > "$dest/base-$stamp.dump"

# PDF chiffrés
docker run --rm -v cse-forms_files:/data:ro -v "$dest":/backup alpine \
  tar czf "/backup/files-$stamp.tar.gz" -C /data .

# Rétention locale : 30 jours
find "$dest" -type f -mtime +30 -delete
```

Planification (crontab root), en dehors de l'heure de purge :

```cron
45 1 * * * /opt/cse-forms-backup/sauvegarde.sh >> /var/log/cse-forms-backup.log 2>&1
```

Copier ensuite les fichiers vers un stockage distant chiffré. Vérifier le nom réel du volume avec `docker volume ls | grep cse-forms`.

### 9.3 Restauration ⚠️

La restauration **écrase** la base et les PDF actuels. Tester la procédure sur un serveur de recette au moins une fois avant l'ouverture, puis chaque année.

```bash
cd /opt/cse-forms && docker compose stop api worker
```

```bash
set -a; . ./.env; set +a
```

```bash
docker compose exec -T postgres pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner < /var/backups/cse-forms/base-AAAAMMJJ-HHMMSS.dump
```

```bash
docker run --rm -v cse-forms_files:/data -v /var/backups/cse-forms:/backup alpine sh -c "rm -rf /data/* && tar xzf /backup/files-AAAAMMJJ-HHMMSS.tar.gz -C /data"
```

```bash
docker compose up -d
```

Le `.env` restauré doit porter la **même** `Storage__EncryptionKey` que lors de la sauvegarde. Après une restauration, des réponses échues depuis la sauvegarde seront purgées à la nuit suivante, ce qui est le comportement attendu.

---

## 10. Mises à jour

```bash
cd /opt/cse-forms && /opt/cse-forms-backup/sauvegarde.sh
```

```bash
git fetch && git log --oneline HEAD..origin/main
```

```bash
git pull --ff-only
```

```bash
docker compose up -d --build
```

```bash
docker compose logs migrate && docker compose ps -a
```

- Les migrations s'appliquent automatiquement (service `migrate`). Elles ne se défont pas automatiquement : en cas de problème, revenir au commit précédent **et** restaurer la sauvegarde faite juste avant (§ 9.3).
- Comparer `.env.production.example` avec `.env` après chaque mise à jour : une nouvelle variable obligatoire bloque le démarrage avec un message `... absent`.
- Images tierces : `postgres:17-alpine` suit les correctifs mineurs à chaque `docker compose pull`. Ne jamais changer la version majeure de PostgreSQL sans procédure de migration (`pg_dump` / `pg_restore`). Gotenberg est épinglé ; sa version se change dans `docker-compose.yml`.
- Mettre à jour régulièrement l'hôte (`apt upgrade`) et Docker. Reconstruire les images (`--build --pull`) pour récupérer les correctifs de sécurité des images .NET :

```bash
docker compose build --pull && docker compose up -d
```

- Nettoyer les anciennes images de temps en temps : `docker image prune`.

---

## 11. Exploitation courante

### 11.1 Surveillance

| Quoi | Comment |
| --- | --- |
| Disponibilité | Superviser `https://<domaine>/health/ready` (Uptime Kuma, sonde du SI...) |
| Échecs de tâches | Emails à `alerts.email` après 3 échecs d'un PDF, d'une notification ou d'un accusé |
| Journaux | `docker compose logs -f --since 1h api worker` ; limités à 5 × 10 Mo par service |
| Espace disque | Volumes `postgres-data`, `files`, dossier des sauvegardes |
| État des conteneurs | `docker compose ps` ; Portainer si utilisé |

Les journaux ne contiennent aucune donnée personnelle : une réponse y est identifiée par son id ou son numéro de dossier.

### 11.2 Tâches régulières

| Fréquence | Action |
| --- | --- |
| Quotidienne (automatique) | Purge des réponses échues et des défis anti-robot (Worker, 3 h 17) ; sauvegarde |
| Hebdomadaire | Lire les alertes ; vérifier qu'aucune réponse ne reste « Nouvelle » trop longtemps (elle ne serait jamais purgée) |
| Mensuelle | Mises à jour (§ 10) ; contrôle de la réussite des sauvegardes |
| À chaque changement d'élus | Écran « Comptes » : création, droits, désactivation des partants ; changer le code d'accès si besoin |
| Annuelle | Test de restauration ; revue des comptes et des droits ; revue du registre des traitements |

### 11.3 Commandes utiles

```bash
docker compose ps -a
```

```bash
docker compose restart api worker
```

```bash
docker compose logs --tail 200 worker
```

```bash
docker compose exec postgres psql -U cseforms -d cseforms
```

Le shell `psql` donne accès à toutes les données personnelles : l'utiliser uniquement en lecture et pour le diagnostic.

### 11.4 Arrêt et suppression

```bash
docker compose down
```

`down` conserve les volumes. ⚠️ `docker compose down -v` **supprime définitivement** la base, les PDF et les clés : ne l'utiliser que pour une désinstallation, après sauvegarde et décision du CSE.

---

## 12. Dépannage

### 12.1 Le démarrage échoue

| Symptôme | Cause probable |
| --- | --- |
| `required variable ... absent` | Variable obligatoire manquante dans `.env` (§ 4) |
| `migrate` en `Exited (1)` | Base injoignable ou mot de passe faux : `docker compose logs migrate postgres` |
| Mot de passe PostgreSQL changé dans `.env` après le premier démarrage | PostgreSQL garde l'ancien : le changer dans la base (`ALTER USER`) ou revenir à l'ancien |
| `api` redémarre en boucle | `docker compose logs api` : clé base64 invalide (`Storage__EncryptionKey`, `ProofOfWork__HmacKey`), réseau `ReverseProxy__KnownNetworks` mal écrit |

### 12.2 Back-office : erreur 400 « HTTPS requis »

L'API ne reconnaît pas la requête comme HTTPS : le proxy n'envoie pas `X-Forwarded-Proto: https`, ou son adresse n'est pas dans `ReverseProxy__KnownNetworks`. Un avertissement est journalisé :

```bash
docker compose logs api | grep -i "back-office refusée"
```

Pour connaître l'adresse vue par l'API, regarder la connexion entrante sur l'hôte pendant une requête :

```bash
sudo ss -tn state established "( sport = :8080 )"
```

ou, si le proxy est conteneurisé, `docker inspect <conteneur-du-proxy>` (section `Networks`). Corriger `.env`, puis :

```bash
docker compose up -d api
```

### 12.3 Formulaire public : « trop de requêtes » (429)

Limitation de débit par IP. Si tous les salariés sont bloqués en même temps, l'API voit une seule IP : même cause qu'au § 12.2. Si un site entier sort par une seule IP publique (NAT d'usine), relever `RateLimiting__PublicSubmit__PermitLimit` (§ 13.1).

### 12.4 Pas d'email

1. `cse-worker mail test --to <adresse>` : affiche la cause.
2. `docker compose logs worker | grep -i job` : type d'erreur des tâches.
3. Vérifier `smtp.*` (`cse settings list`) et `Smtp__Username`/`Smtp__Password`.
4. Le Worker doit pouvoir sortir vers le port SMTP (pare-feu sortant de l'hôte).
5. Aucune règle de notification : `cse notifications show --form <slug>`.

### 12.5 Pas de PDF

`docker compose ps gotenberg` puis `docker compose logs gotenberg worker`. Après correction, régénérer depuis la fiche de la réponse (« Régénérer le PDF »).

### 12.6 Élus déconnectés après chaque redémarrage

Le volume `keys` n'est pas monté ou a été recréé. Vérifier `docker volume ls` et la section `volumes` du service `api`.

### 12.7 Codes de double authentification refusés

Horloge de l'hôte décalée : `timedatectl` doit indiquer `System clock synchronized: yes`.

---

## 13. Référence des variables et paramètres

### 13.1 Variables d'environnement (`.env`)

| Variable | Service | Défaut | Rôle |
| --- | --- | --- | --- |
| `POSTGRES_DB` | postgres, api, worker | — (obligatoire) | Nom de la base |
| `POSTGRES_USER` | postgres, api, worker | — (obligatoire) | Rôle PostgreSQL |
| `POSTGRES_PASSWORD` | postgres, api, worker | — (obligatoire) | Mot de passe, sans `;` ni guillemet |
| `API_BIND` | api | `0.0.0.0` | Adresse d'écoute sur l'hôte |
| `API_PORT` | api | `8080` | Port publié sur l'hôte |
| `ReverseProxy__KnownNetworks` | api | — (obligatoire) | IP du proxy (CIDR, virgules) |
| `Storage__EncryptionKey` | api, worker | — (obligatoire) | Clé AES-256 des PDF, base64 32 octets |
| `ProofOfWork__HmacKey` | api | — (obligatoire) | Clé HMAC des défis anti-robot, base64 32 octets |
| `ProofOfWork__MaxNumber` | api | `50000` | Difficulté du défi (plus haut = plus long sur smartphone) |
| `ProofOfWork__ExpiresMinutes` | api | `20` | Validité d'un défi |
| `RateLimiting__PublicSubmit__PermitLimit` / `__WindowSeconds` | api | `30` / `600` | Soumissions par IP et par fenêtre |
| `RateLimiting__PublicRead__PermitLimit` / `__WindowSeconds` | api | `120` / `60` | Lectures publiques par IP |
| `RateLimiting__Login__PermitLimit` / `__WindowSeconds` | api | `20` / `600` | Tentatives de connexion par IP |
| `Jobs__PollSeconds` | worker | `5` | Cadence de relève des tâches |
| `Jobs__WorkerCount` | worker | `2` | Tâches simultanées |
| `Purge__Cron` | worker | `17 3 * * *` | Horaire de purge (cron, heure de Paris) |
| `Smtp__Username` / `Smtp__Password` | worker | vide | Identifiants SMTP |
| `BootstrapAdmin__Email` / `__Password` / `__Name` | api | vide | Administrateur initial, base sans compte uniquement (§ 4.6) |
| `Smtp__TimeoutSeconds` | worker | `30` | Délai d'expiration SMTP |

Fixées par l'image, à ne pas modifier : `Storage__Path=/var/lib/cse-forms/files`, `DataProtection__KeysPath=/var/lib/cse-forms/keys`, `ASPNETCORE_HTTP_PORTS=8080`, `Gotenberg__Url=http://gotenberg:3000`, `ConnectionStrings__Default` (construite par Compose).

### 13.2 Paramètres en base (`settings`)

| Clé | Rôle |
| --- | --- |
| `cse.name` | Nom du CSE (documents, nom d'expéditeur) |
| `cse.address` | Adresse (multi-lignes) |
| `cse.email` | Contact ; `Reply-To` des accusés |
| `cse.phone` | Téléphone |
| `cse.office` | Composition du bureau (multi-lignes, PDF) |
| `cse.logo` | Logo (`settings logo`) |
| `app.publicBaseUrl` | Adresse publique (liens, QR code, flyer) |
| `smtp.host` | Serveur SMTP |
| `smtp.port` | Port (587 par défaut) |
| `smtp.security` | `starttls` (défaut), `ssl`, `none` |
| `smtp.from` | Adresse d'expédition |
| `alerts.email` | Destinataire des alertes d'échec |

`cse settings list` affiche la liste à jour. Aucun secret n'est stocké en base.

### 13.3 Volumes

| Volume | Monté dans | Contenu | Perte |
| --- | --- | --- | --- |
| `postgres-data` | postgres | Base | Perte de toutes les données |
| `files` | api, worker | PDF chiffrés | PDF à régénérer depuis la fiche |
| `keys` | api | Clés des cookies | Élus déconnectés, appareils de confiance oubliés |

### 13.4 Commandes d'administration

| Commande (conteneur `api`, sauf mention) | Effet |
| --- | --- |
| `db migrate` | Met le schéma à jour (lancé par `migrate`) |
| `users create\|grant\|revoke\|reset-password\|reset-2fa\|list` | Comptes et droits, en secours de l'écran « Comptes » |
| `forms import\|access-code\|list` | Formulaires |
| `sites add\|disable\|enable\|list` | Sites |
| `settings set\|unset\|logo\|list` | Paramètres |
| `notifications set\|show\|remove` | Règles de notification |
| `mail test --to <adresse>` (conteneur `worker`) | Test SMTP |

---

## 14. Check-list de mise en service

**Préalables (CSE)**

- [ ] Décisions de [specs § 5.4](../specs.md#54-décisions-à-soumettre-au-cse) actées : champs, durée de conservation, élus habilités, hébergement
- [ ] Registre des traitements du CSE complété
- [ ] Notice d'information du formulaire relue

**Infrastructure**

- [ ] Serveur en France/UE, disque chiffré, NTP actif, mises à jour automatiques de sécurité
- [ ] Docker et Compose v2 installés
- [ ] Dépôt cloné dans `/opt/cse-forms`
- [ ] Pare-feu : port de l'API joignable par le seul proxy
- [ ] Sortie SMTP autorisée

**Configuration**

- [ ] `.env` créé, `chmod 600`, aucun `a-remplacer`
- [ ] `Storage__EncryptionKey` et `.env` sauvegardés dans le coffre-fort, hors serveur
- [ ] `ReverseProxy__KnownNetworks` = IP exacte du proxy en `/32`
- [ ] `docker compose up -d --build` ; `migrate` en `Exited (0)` ; `/health/ready` OK

**Proxy**

- [ ] Domaine, certificat, redirection HTTP → HTTPS
- [ ] `X-Forwarded-For` et `X-Forwarded-Proto: https` transmis
- [ ] `/admin` affiche la connexion (pas de 400)

**Paramétrage**

- [ ] `cse.*`, logo, `app.publicBaseUrl`
- [ ] Sites créés
- [ ] SMTP réglé, `mail test` reçu, `alerts.email` renseigné
- [ ] Formulaire importé, code d'accès défini
- [ ] Notifications réglées
- [ ] Mot de passe de l'administrateur initial changé, `BootstrapAdmin__Password` vidé dans `.env`
- [ ] Comptes des élus habilités créés, droits attribués, 2FA recommandée

**Exploitation**

- [ ] Sauvegarde quotidienne planifiée, copie distante chiffrée
- [ ] Restauration testée sur un serveur de recette
- [ ] Supervision de `/health/ready`
- [ ] Recette du § 8 passée, réponses de test supprimées
- [ ] QR code et flyer produits, remis aux RH
