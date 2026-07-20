# syntax=docker/dockerfile:1

# =============================================================================
# Stage 1: Build — shared across all targets
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore MCP server project
COPY src/CapabilityModule.Office/CapabilityModule.Office.csproj src/CapabilityModule.Office/
RUN dotnet restore src/CapabilityModule.Office/CapabilityModule.Office.csproj

# Restore CLI project
COPY src/CapabilityModule.Office.Cli/CapabilityModule.Office.Cli.csproj src/CapabilityModule.Office.Cli/
RUN dotnet restore src/CapabilityModule.Office.Cli/CapabilityModule.Office.Cli.csproj

# Restore WebApi project
COPY src/CapabilityModule.Office.WebApi/CapabilityModule.Office.WebApi.csproj src/CapabilityModule.Office.WebApi/
RUN dotnet restore src/CapabilityModule.Office.WebApi/CapabilityModule.Office.WebApi.csproj

COPY src/ src/

# =============================================================================
# Stage 2: MCP module runtime
# =============================================================================
FROM build AS module-build
RUN dotnet publish src/CapabilityModule.Office/CapabilityModule.Office.csproj -c Release -o /app/publish --no-restore
# Publish the CLI binary alongside the MCP server
RUN dotnet publish src/CapabilityModule.Office.Cli/CapabilityModule.Office.Cli.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS module
WORKDIR /app

USER root
RUN mkdir -p /data && chown app:app /data
USER app

COPY --from=module-build --chown=app:app /app/publish .

# Copy database migration scripts so DbInitializer can find them at startup
COPY --chown=app:app db/ db/

ENV ASPNETCORE_URLS=http://+:8080
ENV OFFICE_CLI_ROOT=/data
EXPOSE 8080

ENTRYPOINT ["dotnet", "CapabilityModule.Office.dll"]

# =============================================================================
# Stage 3: WebApi runtime
# =============================================================================
FROM build AS webapi-build
RUN dotnet publish src/CapabilityModule.Office.WebApi/CapabilityModule.Office.WebApi.csproj -c Release -o /app/publish --no-restore
# Publish the CLI binary alongside the WebApi server
RUN dotnet publish src/CapabilityModule.Office.Cli/CapabilityModule.Office.Cli.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS webapi
WORKDIR /app

USER root
RUN mkdir -p /data && chown app:app /data
USER app

COPY --from=webapi-build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV OFFICE_CLI_ROOT=/data
EXPOSE 8080

ENTRYPOINT ["dotnet", "CapabilityModule.Office.WebApi.dll"]