# ── Stage 1: Build ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy all csproj files preserving folder structure (layer cache)
COPY Domain/Domain.csproj Domain/
COPY Services/Services.csproj Services/
COPY Services.Abstraction/Services.Abstraction.csproj Services.Abstraction/
COPY Persistance/Persistence.csproj Persistance/
COPY Presentation/Presentation.csproj Presentation/
COPY Shared/Shared.csproj Shared/
COPY ClinicFlow/ClinicFlow.csproj ClinicFlow/

# Restore using the startup project
RUN dotnet restore ClinicFlow/ClinicFlow.csproj

# Copy everything else
COPY . .

# Publish
RUN dotnet publish ClinicFlow/ClinicFlow.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD curl -fs http://localhost:8080/health || exit 1

USER app

ENTRYPOINT ["dotnet", "ClinicFlow.dll"]