# The .NET API (PLANNING Faz 6, E5). Built in two stages: the SDK image compiles, and only
# the published output goes into the much smaller runtime image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The project files first, so the package restore layer is reused until they change.
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY BtuAsistan.Api/BtuAsistan.Api.csproj BtuAsistan.Api/
RUN dotnet restore BtuAsistan.Api/BtuAsistan.Api.csproj && dotnet tool restore

COPY BtuAsistan.Api/ BtuAsistan.Api/
RUN dotnet publish BtuAsistan.Api/BtuAsistan.Api.csproj --configuration Release --no-restore --output /app

# All migrations as one program (E6), run by the compose "migrate" service with the table
# owner's account (ConnectionStrings__Migrations, read by DesignTimeDbContextFactory). The
# API itself never migrates and never sees that account. Making the bundle creates the
# DbContext once, which wants a connection string; nothing connects and nothing of it is
# kept, the real one is read when the bundle runs.
RUN ConnectionStrings__Migrations="Host=unused-at-build-time" \
    dotnet ef migrations bundle --project BtuAsistan.Api/BtuAsistan.Api.csproj --configuration Release --output /app/efbundle


FROM mcr.microsoft.com/dotnet/aspnet:10.0

# For the health check only; the runtime image has no HTTP client of its own.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production

# The image's own unprivileged user; it listens on 8080 (no root needed for that).
USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --output /dev/null http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "BtuAsistan.Api.dll"]
