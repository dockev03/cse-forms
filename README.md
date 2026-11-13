# Outil de formulaires du CSE

Formulaires publics accessibles par QR code, back-office réservé aux élus, génération de PDF et notifications sans donnée personnelle. La référence fonctionnelle est [`specs.md`](specs.md).

## Structure

| Projet | Rôle |
| --- | --- |
| `src/CseForms.Domain` | Entités et règles métier (formulaire, version, réponse, site, paramètre) |
| `src/CseForms.Application` | Cas d'usage et moteur de formulaires : lecture de la définition JSON, validation et normalisation des réponses ([format](docs/format-definition.md)) |
| `src/CseForms.Infrastructure` | Persistance EF Core et PostgreSQL, migrations |
| `src/CseForms.Api` | API, formulaire public (`/f/{slug}`, fichiers dans `wwwroot`) et back-office |
| `forms` | Fichiers de définition des formulaires, publiés avec `forms import` |
| `src/CseForms.Worker` | Tâches de fond (Hangfire) : génération des PDF, notifications et accusés de réception par email, purge quotidienne des réponses |
| `tests/CseForms.Testing` | Outils de test partagés (base PostgreSQL jetable, données fictives) |
| `tests/*` | Tests xUnit |
| `tests/CseForms.E2E.Tests` | Tests de bout en bout Playwright (navigateur réel) |

## Démarrage en local

Prérequis : SDK .NET 10, Docker.

```bash
cp .env.example .env            # puis remplacer les valeurs
docker compose -f docker-compose.dev.yml up -d   # PostgreSQL et Gotenberg
set -a; . ./.env; set +a        # charge les variables dans le shell
dotnet tool restore
dotnet ef database update -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure
dotnet run --project src/CseForms.Api
dotnet run --project src/CseForms.Worker   # dans un autre terminal : PDF, emails et purge
```

La clé de chiffrement des PDF se génère avec `openssl rand -base64 32` (variable `Storage__EncryptionKey`). Voir [`docs/back-office.md`](docs/back-office.md#pdf). L'API exige aussi une clé pour signer les défis anti-robot, générée de la même façon (`ProofOfWork__HmacKey`) ; voir [`docs/api-publique.md`](docs/api-publique.md#obtenir-un-défi-anti-robot).

Le back-office (`/admin`) exige HTTPS : ses cookies sont `Secure`. En local, lancer l'API avec `ASPNETCORE_URLS=https://127.0.0.1:7214` (certificat de développement : `dotnet dev-certs https`). En production, le service est exposé directement en HTTP et le reverse proxy existant (hors projet) termine le TLS et transmet `X-Forwarded-For` et `X-Forwarded-Proto` ; déclarer son adresse dans `ReverseProxy__KnownNetworks`. Voir [`docs/back-office.md`](docs/back-office.md).

Points de santé : `/health/live` (processus) et `/health/ready` (base joignable). API publique : voir [`docs/api-publique.md`](docs/api-publique.md).

## Déploiement en production

Guide complet (installation, configuration, reverse proxy, sauvegarde, mises à jour, dépannage) : [`docs/mise-en-service.md`](docs/mise-en-service.md).

`docker-compose.yml` lance PostgreSQL, Gotenberg, l'API et le Worker. Il n'y a pas de proxy dans le projet : le reverse proxy existant termine le TLS et transmet les requêtes au port HTTP de l'API, seul port publié.

```bash
cp .env.production.example .env   # puis remplacer chaque « a-remplacer » ; chmod 600 .env
docker compose up -d --build
```

- **Démarrage** : le service `migrate` (`dotnet CseForms.Api.dll db migrate`) met le schéma à jour puis s'arrête ; l'API et le Worker ne démarrent qu'ensuite. Une mise à jour se fait par `git pull` puis la même commande.
- **Reverse proxy** : il cible `http://<hôte>:${API_PORT}` et transmet `X-Forwarded-For` et `X-Forwarded-Proto: https`. Son adresse, vue par l'API, va dans `ReverseProxy__KnownNetworks` ; sans elle, le back-office refuse les connexions (cookies `Secure`). Points de santé : `/health/live`, `/health/ready`.
- **Exposition** : seul le reverse proxy doit joindre le port de l'API (`API_BIND` ou pare-feu). PostgreSQL et Gotenberg sont sur un réseau interne sans accès extérieur ; seul le Worker sort, vers le serveur SMTP.
- **Conteneurs** : API et Worker tournent sans root, système de fichiers en lecture seule, sans privilège. Journaux Docker limités à 5 × 10 Mo par service.
- **Données** : volumes `postgres-data` (base), `files` (PDF chiffrés) et `keys` (clés des cookies du back-office). Sauvegarder base et `files` ensemble, et la clé `Storage__EncryptionKey` à part.
- **Administration** : les commandes ci-dessous se lancent dans le conteneur, par exemple `docker compose exec api dotnet CseForms.Api.dll users list`. Les définitions du dépôt sont dans l'image : `forms import --file /app/forms/inscription.json`.

## Comptes du back-office

Au premier démarrage, l'API crée un administrateur si la base ne contient aucun compte, à partir de `BootstrapAdmin__Email`, `BootstrapAdmin__Password` (12 caractères minimum) et `BootstrapAdmin__Name` (facultatif) lus dans `.env`. Ce mot de passe est provisoire : il est à changer à la première connexion ; les variables sont ensuite ignorées.

Les autres comptes se gèrent dans le back-office, écran « Comptes » (`/admin/comptes`, rôle `admin`) : création avec mot de passe provisoire affiché une seule fois, rôle, formulaires attribués, réinitialisation du mot de passe ou de la double authentification, désactivation ([détail](docs/back-office.md#comptes)). Chaque élu change son mot de passe depuis « Mot de passe » (`/admin/mot-de-passe`).

La ligne de commande reste disponible en secours (par exemple si plus aucun administrateur ne peut se connecter), le mot de passe étant saisi au terminal :

```bash
dotnet run --project src/CseForms.Api -- users create --email elu@example.test --name "Prénom Nom" --role admin
dotnet run --project src/CseForms.Api -- users grant --email elu@example.test --form inscription
dotnet run --project src/CseForms.Api -- users revoke --email elu@example.test --form inscription
dotnet run --project src/CseForms.Api -- users reset-password --email elu@example.test
dotnet run --project src/CseForms.Api -- users reset-2fa --email elu@example.test
dotnet run --project src/CseForms.Api -- users list
```

En conteneur : `docker compose exec api dotnet CseForms.Api.dll users ...`. Rôles : `admin` (tous les formulaires), `editor` et `reader` (formulaires attribués uniquement). Chaque élu peut activer la double authentification depuis « Sécurité du compte » ; `users reset-2fa` (ou le bouton de l'écran « Comptes ») la désactive pour un élu qui a perdu son téléphone et ses codes de secours ([détail](docs/back-office.md#double-authentification)).

## Formulaires, sites et paramètres

En attendant l'éditeur visuel (lot 5), un formulaire se publie depuis son fichier JSON ([format](docs/format-definition.md#fichier-de-formulaire)). Les chemins de fichier sont à donner en absolu.

```bash
for s in Abrest Aulnat Taverny Riom Brive Varennes Nizerolles Soyons; do
  dotnet run --project src/CseForms.Api -- sites add --label "$s"
done
dotnet run --project src/CseForms.Api -- forms import --file "$PWD/forms/inscription.json"
dotnet run --project src/CseForms.Api -- forms access-code --form inscription
dotnet run --project src/CseForms.Api -- forms list
dotnet run --project src/CseForms.Api -- settings set --key cse.name --value "CSE NSE"
dotnet run --project src/CseForms.Api -- settings set --key cse.office < bureau.txt
dotnet run --project src/CseForms.Api -- settings logo --file "$PWD/logo-cse.png"
dotnet run --project src/CseForms.Api -- settings list
```

- `forms import` crée le formulaire ou publie une nouvelle version si la définition ou le modèle PDF a changé ; les réponses déjà reçues restent rattachées à leur version. Le modèle PDF (`pdfTemplate`, par exemple `forms/inscription.pdf.html`) est lu dans le même dossier que le fichier JSON et contrôlé à l'import ([syntaxe](docs/format-definition.md#modèle-pdf)). Le code d'accès est demandé au terminal au premier import d'un formulaire en mode `pin`, jamais passé en argument.
- Fermeture, réouverture, dates d'ouverture et de fermeture, nombre maximal de réponses et changement du code d'accès se font dans le back-office, écran « Réglages » du formulaire (`/admin/formulaires/{id}/reglages`, rôles `admin` et `editor`) ; `forms access-code` reste disponible en secours ([détail](docs/back-office.md#réglages-dun-formulaire)).
- `sites disable --label <libellé>` retire un site des listes sans toucher aux réponses existantes ; `sites enable` le réactive.
- `settings` n'accepte que les clés connues (`settings list` les affiche) : coordonnées, bureau et logo du CSE, adresse publique du site. Aucun secret n'y est stocké.
- `settings logo` n'accepte qu'une image PNG ou JPEG de 256 Ko au plus (pas de SVG, qui peut contenir du script) ; elle est stockée en base et intégrée aux documents, sans lien externe.

## Diffusion

QR code (PNG, SVG), QR code par site et flyer PDF se produisent dans le back-office : `/admin/formulaires/{id}/diffusion`, rôles `admin` et `editor`. Voir [`docs/back-office.md`](docs/back-office.md#diffusion-qr-code-et-flyer). Prérequis : paramètre `app.publicBaseUrl` et, pour le flyer, `Gotenberg__Url` dans la configuration de l'API.

## Conservation et purge

Chaque formulaire fixe sa durée de conservation (`retentionDays`, 180 jours pour l'inscription). Elle court à partir du traitement de la réponse : dès qu'un élu la passe de « Nouvelle » à « Saisie dans le logiciel CSE » ou « Archivée ». Le Worker supprime chaque nuit (3 h 17, heure de Paris) les réponses échues avec leur PDF, et le trace dans le journal d'audit ; il efface aussi les défis anti-robot expirés. Horaire modifiable par `Purge__Cron` (expression cron, heure de Paris). Voir [`docs/back-office.md`](docs/back-office.md#conservation-et-purge).

## Emails

Les emails partent du Worker, par SMTP direct (MailKit), jamais pendant la soumission. Voir [`docs/back-office.md`](docs/back-office.md#emails).

```bash
dotnet run --project src/CseForms.Api -- settings set --key smtp.host --value smtp.example.test
dotnet run --project src/CseForms.Api -- settings set --key smtp.port --value 587
dotnet run --project src/CseForms.Api -- settings set --key smtp.security --value starttls
dotnet run --project src/CseForms.Api -- settings set --key smtp.from --value formulaires@example.test
dotnet run --project src/CseForms.Api -- settings set --key alerts.email --value admin@example.test
dotnet run --project src/CseForms.Api -- settings set --key app.publicBaseUrl --value https://formulaires.example.test
dotnet run --project src/CseForms.Api -- notifications set --form inscription --to elu1@example.test,elu2@example.test \
  --subject "Nouvelle inscription n° {numero}, site {site}"
dotnet run --project src/CseForms.Api -- notifications show --form inscription
```

Test des réglages SMTP, depuis le Worker qui détient les identifiants (en conteneur : `docker compose exec worker dotnet CseForms.Worker.dll mail test --to ...`) :

```bash
dotnet run --project src/CseForms.Worker -- mail test --to admin@example.test
```

L'email de test rappelle le serveur, le port, le chiffrement et l'expéditeur, sans identifiant. En cas d'échec, la commande affiche la cause (serveur injoignable, authentification refusée...) et rend un code d'erreur.

- Identifiant et mot de passe SMTP : variables `Smtp__Username` et `Smtp__Password` du Worker, jamais en base. Sans chiffrement (`smtp.security` à `none`), l'authentification est refusée.
- `notifications set` remplace la règle du formulaire ; `--body-file` donne un corps personnalisé. Variables : `{formulaire}`, `{numero}`, `{date}`, `{site}`, `{lien}` ; une variable inconnue est refusée. `notifications remove` coupe les notifications du formulaire.
- L'accusé de réception se règle dans le fichier du formulaire (`receipt`, voir le [format](docs/format-definition.md#accusé-de-réception)).

## Tests

```bash
set -a; . ./.env; set +a
dotnet test
```

Tests du moteur client et de la preuve de travail (Node 22 ou plus, sans dépendance npm) :

```bash
node --test tests/js/*.test.mjs
```

Les tests d'intégration créent une base PostgreSQL jetable par classe de test à partir de `CSEFORMS_TEST_POSTGRES`, puis la suppriment. Uniquement des données fictives.

### Tests de bout en bout (Playwright)

`tests/CseForms.E2E.Tests` pilote un vrai navigateur : formulaire public sur smartphone, back-office (connexion, droits, liste, fiche) et PDF de bout en bout. Ils sont ignorés par `dotnet test` tant que `CSEFORMS_E2E=1` n'est pas défini.

L'environnement de test crée une base jetable, la remplit par la ligne de commande d'administration (sites, formulaire d'inscription, comptes fictifs aux mots de passe tirés au hasard), puis lance l'API en HTTPS avec un certificat auto-signé jetable et le Worker en processus séparés. Gotenberg doit tourner (`docker-compose.dev.yml`).

```bash
dotnet build tests/CseForms.E2E.Tests
```

```bash
pwsh tests/CseForms.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

```bash
set -a; . ./.env; set +a
CSEFORMS_E2E=1 dotnet test tests/CseForms.E2E.Tests --no-build
```

| Variable | Rôle |
| --- | --- |
| `CSEFORMS_E2E` | `1` pour lancer les tests de bout en bout |
| `CSEFORMS_TEST_GOTENBERG` | Adresse de Gotenberg (par défaut `http://127.0.0.1:3000`) |
| `CSEFORMS_E2E_CHROMIUM` | Chemin d'un Chromium déjà installé, à la place de celui de Playwright (facultatif) |
| `CSEFORMS_E2E_ARTIFACTS` | Dossier des traces d'échec (par défaut `TestResults/playwright`) |

Un scénario en échec laisse une trace Playwright (à ouvrir avec `pwsh tests/CseForms.E2E.Tests/bin/Debug/net10.0/playwright.ps1 show-trace <fichier.zip>`), une capture et les journaux de l'API et du Worker.

## Intégration continue

Chaque push sur GitHub lance `.github/workflows/ci.yml` ; le résultat s'affiche sur le commit et sur la PR qui le porte :

- compilation et tests xUnit, avec un PostgreSQL jetable propre au job (aucun secret réel) ;
- tests du moteur client sous Node 22 ;
- tests de bout en bout Playwright (Chromium), avec PostgreSQL et Gotenberg jetables ; traces des échecs conservées 7 jours ;
- analyse Trivy des dépendances NuGet (directes et transitives) et des secrets : la CI échoue sur une vulnérabilité `HIGH` ou `CRITICAL` corrigeable.

Les actions sont épinglées sur un SHA de commit et le workflow n'a que le droit de lecture sur le dépôt.

## Nouvelle migration

```bash
dotnet ef migrations add <Nom> -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure -o Persistence/Migrations
```
