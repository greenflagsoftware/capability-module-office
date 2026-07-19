@echo off
REM Convenience script for Windows cmd.exe
REM Usage: mcp-test list|create|read|info
docker compose exec module dotnet /app/CapabilityModule.Office.Cli.dll docx %*