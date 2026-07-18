# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore MCP server project
COPY src/AgentDock.Office/AgentDock.Office.csproj src/AgentDock.Office/
RUN dotnet restore src/AgentDock.Office/AgentDock.Office.csproj

# Restore CLI project
COPY src/AgentDock.Office.Cli/AgentDock.Office.Cli.csproj src/AgentDock.Office.Cli/
RUN dotnet restore src/AgentDock.Office.Cli/AgentDock.Office.Cli.csproj

COPY src/ src/

# Publish the MCP server
RUN dotnet publish src/AgentDock.Office/AgentDock.Office.csproj -c Release -o /app/publish --no-restore

# Publish the CLI binary and its dependencies alongside the MCP server.
# The two assemblies have different names (AgentDock.Office.dll vs
# AgentDock.Office.Cli.dll) so they coexist in the same directory.
RUN dotnet publish src/AgentDock.Office.Cli/AgentDock.Office.Cli.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user — the ASP.NET base image ships a pre-created 'app' user/group (UID/GID 64198).

# Create a writable data directory for the module's restricted root, then
# switch to the non-root user. The CLI resolves paths relative to this dir.
USER root
RUN mkdir -p /data && chown app:app /data
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV OFFICE_CLI_ROOT=/data
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentDock.Office.dll"]