# Multi-stage build for the Flowamaz API (.NET 10). Build context is ./backend.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the API project (not the .sln — the .sln references test projects whose
# csproj files are not copied here). Copying csproj files first keeps the restore layer cached.
COPY Flowamaz.Api/*.csproj Flowamaz.Api/
COPY Flowamaz.Core/*.csproj Flowamaz.Core/
COPY Flowamaz.Application/*.csproj Flowamaz.Application/
COPY Flowamaz.Infrastructure/*.csproj Flowamaz.Infrastructure/
RUN dotnet restore Flowamaz.Api/Flowamaz.Api.csproj

COPY . .
RUN dotnet publish Flowamaz.Api/Flowamaz.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed for the container HEALTHCHECK (install as root before dropping privileges).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run as the non-root user the .NET base image ships (APP_UID=1654).
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flowamaz.Api.dll"]
