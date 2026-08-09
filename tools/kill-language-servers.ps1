# Kills anything this project may have left running: the MCP server host and the language
# servers it owns. A language server left alive holds handles on the workspace, which ADR-008
# requires to be deletable and regenerable from scratch — and it also locks the build output.
#
#   pwsh tools/kill-language-servers.ps1

$ErrorActionPreference = 'SilentlyContinue'

Get-Process -Name 'Microsoft.CodeAnalysis.LanguageServer' | Stop-Process -Force

Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
    Where-Object { $_.CommandLine -like '*typescript-language-server*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*Orchestrator.LspServer.dll*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

Write-Output 'Language servers and MCP server hosts stopped.'
