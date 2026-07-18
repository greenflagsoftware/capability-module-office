# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only the project file, so the dependency layer caches independently
# of source-code edits.
COPY src/AgentDock.Office/AgentDock.Office.csproj src/AgentDock.Office/
RUN dotnet restore src/AgentDock.Office/AgentDock.Office.csproj

COPY src/ src/
RUN dotnet publish src/AgentDock.Office/AgentDock.Office.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user — the ASP.NET base image ships a pre-created 'app' user/group (UID/GID 64198).
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentDock.Office.dll"]
