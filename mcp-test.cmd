@echo off
REM Convenience script for Windows cmd.exe
REM Usage: mcp-test list|create|read|info
docker compose exec module dotnet /app/AgentDock.Office.Cli.dll docx %*