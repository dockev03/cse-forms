# Outil de formulaires du CSE

Formulaires publics accessibles par QR code, back-office réservé aux élus, génération de PDF et notifications sans donnée personnelle. La référence fonctionnelle est [`specs.md`](specs.md).

## Structure

| Projet | Rôle |
| --- | --- |
| `src/CseForms.Domain` | Entités et règles métier (formulaire, version, réponse, site, paramètre) |
| `src/CseForms.Application` | Cas d'usage (à venir) |
| `src/CseForms.Infrastructure` | Persistance EF Core et PostgreSQL, migrations |
| `src/CseForms.Api` | API, formulaire public et back-office |
| `src/CseForms.Worker` | Tâches de fond : PDF, emails, purge (à venir) |
| `tests/*` | Tests xUnit |

## Démarrage en local

Prérequis : SDK .NET 10, Docker.

```bash
cp .env.example .env            # puis remplacer les valeurs
docker compose -f docker-compose.dev.yml up -d
set -a; . ./.env; set +a        # charge les variables dans le shell
dotnet tool restore
dotnet ef database update -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure
dotnet run --project src/CseForms.Api
```

Points de santé : `/health/live` (processus) et `/health/ready` (base joignable).

## Tests

```bash
set -a; . ./.env; set +a
dotnet test
```

Les tests d'intégration créent une base PostgreSQL jetable par classe de test à partir de `CSEFORMS_TEST_POSTGRES`, puis la suppriment. Uniquement des données fictives.

## Nouvelle migration

```bash
dotnet ef migrations add <Nom> -p src/CseForms.Infrastructure -s src/CseForms.Infrastructure -o Persistence/Migrations
```
