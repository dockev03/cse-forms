# Images de production de l'API et du Worker, construites depuis la racine du dépôt :
#   docker build --target api -t cse-forms-api .
#   docker build --target worker -t cse-forms-worker .
# Les versions publiées sont construites par .github/workflows/release.yml et poussées sur GHCR ;
# docker-compose.build.yml construit localement. Exécution sans root (utilisateur « app » des images .NET).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration à part : la couche reste en cache tant que les projets ne changent pas.
COPY global.json CseForms.slnx ./
COPY src/CseForms.Domain/CseForms.Domain.csproj src/CseForms.Domain/
COPY src/CseForms.Application/CseForms.Application.csproj src/CseForms.Application/
COPY src/CseForms.Infrastructure/CseForms.Infrastructure.csproj src/CseForms.Infrastructure/
COPY src/CseForms.Api/CseForms.Api.csproj src/CseForms.Api/
COPY src/CseForms.Worker/CseForms.Worker.csproj src/CseForms.Worker/
RUN dotnet restore src/CseForms.Api/CseForms.Api.csproj \
 && dotnet restore src/CseForms.Worker/CseForms.Worker.csproj

COPY src/ src/
# Version publiée (tag vX.Y.Z sans le « v »), fournie par la CI de publication ; 0.0.0-dev en local.
ARG VERSION=0.0.0-dev
RUN dotnet publish src/CseForms.Api/CseForms.Api.csproj --no-restore -c Release -o /out/api -p:Version="$VERSION" \
 && dotnet publish src/CseForms.Worker/CseForms.Worker.csproj --no-restore -c Release -o /out/worker -p:Version="$VERSION"

# Socle commun : dossiers des volumes créés ici pour appartenir à « app » dès leur premier montage.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    Storage__Path=/var/lib/cse-forms/files \
    DataProtection__KeysPath=/var/lib/cse-forms/keys
# libgssapi-krb5-2 : chargée par Npgsql au démarrage (message d'erreur sinon, même sans Kerberos).
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /var/lib/cse-forms/files /var/lib/cse-forms/keys \
 && chown -R "$APP_UID" /var/lib/cse-forms \
 && chmod -R 700 /var/lib/cse-forms
WORKDIR /app

FROM runtime AS api
COPY --from=build /out/api ./
# Définitions des formulaires, pour `forms import --file /app/forms/<fichier>.json`.
COPY forms/ ./forms/
USER $APP_UID
# HTTP seul : le reverse proxy existant termine le TLS.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CseForms.Api.dll"]

FROM runtime AS worker
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "CseForms.Worker.dll"]
