# ── Stage 1: Build ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy all csproj files preserving folder structure (layer cache)
COPY Domain/Domain.csproj Domain/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
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

# The EF migration bundle, run by the compose migrate service (docs/v2/plans/P0.md PR 11). Building it reads the
# model through the host, which refuses to start without a connection string naming the V2 database; nothing
# connects to it here. dotnet-ef matches the EF Core version in the projects
RUN dotnet tool install --global dotnet-ef --version 10.0.13
ENV PATH="$PATH:/root/.dotnet/tools"
RUN ConnectionStrings__DefaultConnection="Server=unused;Database=ClinicFlowV2" \
    dotnet ef migrations bundle \
    --project Persistance/Persistence.csproj \
    --startup-project ClinicFlow/ClinicFlow.csproj \
    --configuration Release \
    --output /app/publish/efbundle

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# curl for the HEALTHCHECK below; the aspnet image doesn't ship it
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD curl -fs http://localhost:8080/health/live || exit 1

USER app

ENTRYPOINT ["dotnet", "ClinicFlow.dll"]