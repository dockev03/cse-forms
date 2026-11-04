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
| `src/CseForms.Worker` | Tâches de fond (Hangfire) : génération des PDF ; emails et purge à venir |
| `tests/CseForms.Testing` | Outils de test partagés (base PostgreSQL jetable, données fictives) |
| `tests/*` | Tests xUnit |

## Démarrage en local

Prérequis : SDK .NET 10, Docker.

```bash
cp .env.example .env            # puis remplacer les valeurs
docker compose -f docker-compose.dev.yml up -d   # PostgreSQL et Gotenberg
set -a; . ./.env; set +a        # charge les variables dans le shell
dotnet tool restore
dotnet ef database update -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure
dotnet run --project src/CseForms.Api
dotnet run --project src/CseForms.Worker   # dans un autre terminal : génération des PDF
```

La clé de chiffrement des PDF se génère avec `openssl rand -base64 32` (variable `Storage__EncryptionKey`). Voir [`docs/back-office.md`](docs/back-office.md#pdf).

Le back-office (`/admin`) exige HTTPS : ses cookies sont `Secure`. En local, lancer l'API avec `ASPNETCORE_URLS=https://127.0.0.1:7214` (certificat de développement : `dotnet dev-certs https`). En production, Traefik termine le TLS et transmet `X-Forwarded-Proto`. Voir [`docs/back-office.md`](docs/back-office.md).

Points de santé : `/health/live` (processus) et `/health/ready` (base joignable). API publique : voir [`docs/api-publique.md`](docs/api-publique.md).

## Comptes du back-office

Aucun compte n'existe par défaut. Les comptes se gèrent en ligne de commande, le mot de passe (12 caractères minimum) étant saisi au terminal :

```bash
dotnet run --project src/CseForms.Api -- users create --email elu@example.test --name "Prénom Nom" --role admin
dotnet run --project src/CseForms.Api -- users grant --email elu@example.test --form inscription
dotnet run --project src/CseForms.Api -- users revoke --email elu@example.test --form inscription
dotnet run --project src/CseForms.Api -- users list
```

En conteneur : `docker compose exec api dotnet CseForms.Api.dll users ...`. Rôles : `admin` (tous les formulaires), `editor` et `reader` (formulaires attribués uniquement).

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
dotnet run --project src/CseForms.Api -- settings list
```

- `forms import` crée le formulaire ou publie une nouvelle version si la définition a changé ; les réponses déjà reçues restent rattachées à leur version. Le code d'accès est demandé au terminal au premier import d'un formulaire en mode `pin`, jamais passé en argument.
- `sites disable --label <libellé>` retire un site des listes sans toucher aux réponses existantes ; `sites enable` le réactive.
- `settings` n'accepte que les clés connues (`settings list` les affiche) : coordonnées et bureau du CSE, adresse publique du site. Aucun secret n'y est stocké.

## Tests

```bash
set -a; . ./.env; set +a
dotnet test
```

Tests du moteur client (Node 22 ou plus, sans dépendance npm) :

```bash
node --test tests/js/*.test.mjs
```

Les tests d'intégration créent une base PostgreSQL jetable par classe de test à partir de `CSEFORMS_TEST_POSTGRES`, puis la suppriment. Uniquement des données fictives.

## Intégration continue

Chaque push sur GitHub lance `.github/workflows/ci.yml` ; le résultat s'affiche sur le commit et sur la PR qui le porte :

- compilation et tests xUnit, avec un PostgreSQL jetable propre au job (aucun secret réel) ;
- tests du moteur client sous Node 22 ;
- analyse Trivy des dépendances NuGet (directes et transitives) et des secrets : la CI échoue sur une vulnérabilité `HIGH` ou `CRITICAL` corrigeable.

Les actions sont épinglées sur un SHA de commit et le workflow n'a que le droit de lecture sur le dépôt.

## Nouvelle migration

```bash
dotnet ef migrations add <Nom> -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure -o Persistence/Migrations
```
