#!/usr/bin/env pwsh
#
# A wizard - walks a human through a manual procedure step by step.
# PowerShell port of xrmtoolbox-submission-setup.sh (same stages/values).
#
# -----------------------------------------------------------------------
# Wizard library - delightful, consistent UX. Do not hand-edit unless you're
# fixing the library itself; author stages below the marker.
# -----------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

$EnvFile = if ($env:ENV_FILE) { $env:ENV_FILE } else { '.env' }
$script:StageIndex = 0
$script:TotalStages = 0
$script:WrittenEnv = @()
$script:Skipped = @()

function Clear-Screen {
  if ($Host.UI.SupportsVirtualTerminal -or $env:TERM) { Clear-Host }
}

function Show-Banner([string]$Title) {
  Clear-Screen
  Write-Host ""
  Write-Host "  $Title" -ForegroundColor Blue -NoNewline
  Write-Host ""
  Write-Host "  $script:TotalStages stages" -ForegroundColor DarkGray
  Write-Host ""
  Write-Host "  You drive the browser; this wizard tells you exactly what to do and" -ForegroundColor DarkGray
  Write-Host "  captures the values you copy back. Stop any time with Ctrl-C and re-run" -ForegroundColor DarkGray
  Write-Host "  later - it remembers values already saved." -ForegroundColor DarkGray
  Wait-Continue "Ready to start?"
}

function Start-Stage([string]$Name) {
  Clear-Screen
  $script:StageIndex++
  Write-Host ""
  Write-Host "> Stage $($script:StageIndex)/$($script:TotalStages) . $Name" -ForegroundColor Blue
}

function Say([string]$Text) { Write-Host "  $Text" }
function Step([string]$Text) { Write-Host "  - $Text" -ForegroundColor Blue }
function Note([string]$Text) { Write-Host "  $Text" -ForegroundColor DarkGray }
function Warn([string]$Text) { Write-Host "  ! $Text" -ForegroundColor Yellow }

function Open-Url([string]$Url) {
  Write-Host "  -> opening " -ForegroundColor Green -NoNewline
  Write-Host $Url
  try { Start-Process $Url | Out-Null } catch { Warn "couldn't open a browser - visit it manually: $Url" }
}

function Wait-Continue([string]$Message = "Press Enter to continue") {
  Write-Host "  $Message " -ForegroundColor DarkGray -NoNewline
  [void](Read-Host)
}

function Confirm-Prompt([string]$Question) {
  Write-Host "  ? $Question [y/N] " -ForegroundColor Yellow -NoNewline
  $reply = Read-Host
  return $reply -match '^[Yy]'
}

function Get-ExistingEnvValue([string]$Key) {
  if (-not (Test-Path $EnvFile)) { return $null }
  $line = Get-Content $EnvFile | Where-Object { $_ -match "^$Key=" } | Select-Object -Last 1
  if (-not $line) { return $null }
  return $line.Substring($Key.Length + 1)
}

function Read-Value([string]$Key, [string]$Prompt) {
  $current = Get-ExistingEnvValue $Key
  if ($current) {
    Write-Host "  $Prompt " -NoNewline
    Write-Host "[Enter keeps current] " -ForegroundColor DarkGray -NoNewline
  } else {
    Write-Host "  $Prompt " -NoNewline
  }
  $input = Read-Host
  if ([string]::IsNullOrEmpty($input) -and $current) { return $current }
  return $input
}

function Read-Secret([string]$Key, [string]$Prompt) {
  $current = Get-ExistingEnvValue $Key
  if ($current) {
    Write-Host "  $Prompt " -NoNewline
    Write-Host "[Enter keeps current] " -ForegroundColor DarkGray -NoNewline
  } else {
    Write-Host "  $Prompt " -NoNewline
  }
  $secure = Read-Host -AsSecureString
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
  if ([string]::IsNullOrEmpty($plain) -and $current) { return $current }
  return $plain
}

function Write-EnvValue([string]$Key, [string]$Value) {
  if (-not (Test-Path $EnvFile)) { New-Item -ItemType File -Path $EnvFile | Out-Null }
  $lines = @(Get-Content $EnvFile | Where-Object { $_ -notmatch "^$Key=" })
  $lines += "$Key=$Value"
  Set-Content -Path $EnvFile -Value $lines
  $script:WrittenEnv += $Key
  Write-Host "  OK wrote " -ForegroundColor Green -NoNewline
  Write-Host "$Key -> $EnvFile"
}

function Show-Finish {
  Clear-Screen
  Write-Host ""
  Write-Host "  OK Setup complete" -ForegroundColor Green
  if ($script:WrittenEnv.Count -gt 0) {
    Note "wrote $($script:WrittenEnv.Count) value(s) to ${EnvFile}: $($script:WrittenEnv -join ', ')"
  }
  if ($script:Skipped.Count -gt 0) {
    Write-Host ""
    Warn "still to do by hand:"
    foreach ($s in $script:Skipped) { Note "  - $s" }
  }
  Write-Host ""
}

# -----------------------------------------------------------------------
# STAGES
# -----------------------------------------------------------------------

$script:TotalStages = 5

Show-Banner "XrmToolBox Tool Library submission setup"

# -- Stage 1: NuGet.org Trusted Publishing policy ------------------------
Start-Stage "NuGet.org - Trusted Publishing policy"
Say "nuget.org now steers new accounts toward Trusted Publishing (OIDC via"
Say "GitHub Actions) instead of long-lived classic API keys - no secret to"
Say "manage or leak. A workflow now exists in this repo: publish-nuget.yml."
Open-Url "https://www.nuget.org/account/trustedpublishing"
Step "If you already created policies here, DELETE any broad one with Glob Pattern '*' - it would trust the workflow to push ANY package you own."
Step "On the remaining (or a new) policy, set these fields exactly:"
Step "  Repository Owner: martintoelk   <- your GitHub LOGIN, not your display name 'Martin Toelk'"
Step "  Repository:       Modernized-BU-Security-Role-Assigner"
Step "  Workflow File:    publish-nuget.yml   <- filename only, no path"
Step "  Glob Pattern:     BuMatrixSecurityRoleAssigner   <- must match the package Id below"
Step "Save. It stays 'pending' (7-day window) until the workflow runs once and successfully pushes a package."
Wait-Continue "Policy fields corrected and saved?"
Note "No API key to capture here - the workflow exchanges a short-lived one at run time."

# -- Stage 2: package Id --------------------------------------------------
Start-Stage "Pick the NuGet package Id"
Say "This is the literal Id nuget.org and the XrmToolBox portal will both use to"
Say "identify the plugin. Convention: no spaces, matches the assembly name."
Say "Suggested: BuMatrixSecurityRoleAssigner (matches the rewrite's new plugin name)."
$NugetPackageId = Read-Value "NUGET_PACKAGE_ID" "Package Id to use [Enter for BuMatrixSecurityRoleAssigner]:"
if ([string]::IsNullOrEmpty($NugetPackageId)) { $NugetPackageId = "BuMatrixSecurityRoleAssigner" }
Write-EnvValue "NUGET_PACKAGE_ID" $NugetPackageId
Note "The build session's .nuspec should set <id>$NugetPackageId</id> to match."

# -- Stage 3: public project URL -------------------------------------------
Start-Stage "Confirm the public Project URL"
Say "The .nuspec needs a public <projectUrl>. The repo is already public."
$NugetProjectUrlDefault = "https://github.com/martintoelk/Modernized-BU-Security-Role-Assigner"
$NugetProjectUrl = Read-Value "NUGET_PROJECT_URL" "Project URL to use [Enter for $NugetProjectUrlDefault]:"
if ([string]::IsNullOrEmpty($NugetProjectUrl)) { $NugetProjectUrl = $NugetProjectUrlDefault }
Write-EnvValue "NUGET_PROJECT_URL" $NugetProjectUrl

# -- Stage 4: XrmToolBox portal account ------------------------------------
Start-Stage "XrmToolBox portal - account"
Say "Separate account from NuGet.org - this is what an admin reviews your"
Say "submission under."
Open-Url "https://www.xrmtoolbox.com/SignIn"
Step "Sign in, or register a new account if you don't have one."
Wait-Continue "Signed in to the XrmToolBox portal?"

# -- Stage 5: register the package for review ------------------------------
Start-Stage "Register the package on the XrmToolBox portal"
Warn "This step needs the package LIVE on nuget.org first - it doesn't exist yet."
Say "BuMatrixSecurityRoleAssigner.nuspec exists (Id: $NugetPackageId), and .github/workflows/publish-nuget.yml"
Say "can pack + push it via Trusted Publishing. Still TODO before it can run for real:"
Say "  - confirm the min XrmToolBox host version in the nuspec's <dependency> (marked TODO)"
Say "  - resolve the open decisions in CLAUDE.md (rename, classic-BU toggle, Core/UI split)"
Say "Once ready: Actions tab -> 'Publish NuGet package' -> Run workflow -> enter a version."
if (Confirm-Prompt "Is $NugetPackageId already live on nuget.org (nuget.org/packages/$NugetPackageId)?") {
  Open-Url "https://www.xrmtoolbox.com/plugins/new/"
  Step "Enter the package Id ($NugetPackageId) - the portal auto-parses metadata from NuGet."
  Step "Submit. An admin reviews it manually; approval can take a few days."
  Wait-Continue "Submitted for review?"
} else {
  $script:Skipped += "Register $NugetPackageId at https://www.xrmtoolbox.com/plugins/new/ - do this once the build session has pushed the package to nuget.org"
  Note "Skipping for now - re-run this wizard (or just visit the URL above) once the package is on nuget.org."
}

Show-Finish
