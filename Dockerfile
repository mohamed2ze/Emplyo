# Multi-stage build for the Emplyo web application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Emplyo.sln ./
COPY src/Emplyo.Domain/Emplyo.Domain.csproj src/Emplyo.Domain/
COPY src/Emplyo.Application/Emplyo.Application.csproj src/Emplyo.Application/
COPY src/Emplyo.Infrastructure/Emplyo.Infrastructure.csproj src/Emplyo.Infrastructure/
COPY src/Emplyo.Web/Emplyo.Web.csproj src/Emplyo.Web/
RUN dotnet restore src/Emplyo.Web/Emplyo.Web.csproj

COPY . .
RUN dotnet publish src/Emplyo.Web/Emplyo.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is used only by the HEALTHCHECK below; the base image ships without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

# Persistent locations for uploaded documents and Data Protection keys. Mount
# volumes over these paths (see docker-compose.yml). Both must remain writable
# by the app user so documents survive redeploys and sessions stay valid.
RUN mkdir -p /var/lib/emplyo/documents /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /var/lib/emplyo /home/app/.aspnet

EXPOSE 8080

# Run as the unprivileged "app" user (UID 1654) provided by the base image.
USER app

# Liveness/readiness probe against the application's own /health endpoint.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Emplyo.Web.dll"]
