# Home Control als Container-Image, gebaut in drei Stufen (Multi-Stage). Erklärung: docs/DOCKER.md
#
#   1. frontend  Node baut die React-Oberfläche (npm ci, npm run build)
#   2. backend   .NET SDK veröffentlicht die API (dotnet publish)
#   3. final     schlankes aspnet-Image: nur die Laufzeit, die API und die gebaute Oberfläche
#
# Nur Stufe 3 landet im fertigen Image. Node, das SDK, Quellcode und Zwischendateien bleiben in den
# Bau-Stufen zurück. Geheimnisse kommen NIE ins Image, sondern erst beim Start über .env (docker-compose.yml).

# ---------- 1. Oberfläche bauen ----------
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
# Erst nur die Paketlisten kopieren: Solange sie sich nicht ändern, nimmt Docker die Schicht mit
# node_modules aus dem Cache, und ein neuer Build nach einer Codeänderung geht schnell.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---------- 2. API veröffentlichen ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
# Gleicher Trick wie oben: erst die Projektdatei, dann restore (Pakete), dann der restliche Code.
COPY backend/HomeControl.Api/HomeControl.Api.csproj backend/HomeControl.Api/
RUN dotnet restore backend/HomeControl.Api/HomeControl.Api.csproj
COPY backend/HomeControl.Api/ backend/HomeControl.Api/
# Ohne .git im Build kennt das SDK den Commit nicht. GIT_COMMIT kommt von außen (docker-compose.yml),
# daraus wird "1.0.0+<Commit>" für die Einstellungsseite (Info).
ARG GIT_COMMIT=
RUN dotnet publish backend/HomeControl.Api/HomeControl.Api.csproj -c Release -o /app --no-restore \
    -p:SourceRevisionId=${GIT_COMMIT}

# ---------- 3. Fertiges Image ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# Ordner für die Volumes: Datenbank (/data) und Data-Protection-Schlüssel (/keys). Sie gehören dem
# Benutzer "app" (im aspnet-Image schon vorhanden), damit die App ohne root hineinschreiben kann.
RUN mkdir -p /data /keys && chown app:app /data /keys
COPY --from=backend /app ./
COPY --from=frontend /src/frontend/dist ./wwwroot

# Feste, ungeheime Einstellungen für den Container. Alles Geheime steht in .env (nie im Image).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__DefaultConnection="Data Source=/data/homecontrol.db" \
    Climate__DatabasePath=/data/homecontrol-climate.db \
    Scale__SettingsPath=/data/scale-settings.json \
    HomeControl__DataProtectionKeysPath=/keys

# Nicht als root: Bricht jemand über eine Lücke in die App ein, hat er nur die Rechte von "app",
# nicht die des ganzen Containers. $APP_UID (1654) setzt das aspnet-Image selbst.
USER $APP_UID

# Der Standard-Port im aspnet-Image (ASPNETCORE_HTTP_PORTS=8080). Über 1024, deshalb ohne root erlaubt.
EXPOSE 8080

# Alle 30 s fragt Docker, ob die App gesund ist (siehe backend/HomeControl.Api/Status/Health.cs).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "HomeControl.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "HomeControl.Api.dll"]


