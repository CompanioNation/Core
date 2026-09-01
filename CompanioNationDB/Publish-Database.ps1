<#
.SYNOPSIS
	Publishes the CompanioNationDB schema to a SQL Server database.

.DESCRIPTION
	Publishes an already-built CompanioNationDB DACPAC to a SQL Server database
	with SqlPackage. The DACPAC is produced by building the CompanioNationDB
	project (its Debug build auto-publishes to LocalDB afterward).

	This replaces the Visual Studio "Publish" feature that is no longer
	available for the SDK-style (MSBuild.Sdk.SqlProj) database project.

	By default it targets the local LocalDB database:
		(localdb)\MSSqlLocalDB — CompanioNationDB

	Pass -ConnectionString to publish to Azure SQL or any other target instead.

.PARAMETER ConnectionString
	Optional. Target connection string. Defaults to LocalDB.

.PARAMETER Configuration
	Build configuration whose output DACPAC is published. Default is Release.

.PARAMETER DacpacPath
	Explicit path to a DACPAC file to publish.

.PARAMETER BlockOnPossibleDataLoss
	Refuse to deploy when the change could result in data loss. By default this
	is allowed, matching the CI pipeline (/p:BlockOnPossibleDataLoss=false).

.PARAMETER NonInteractive
	Proceed without prompting for confirmation.

.EXAMPLE
	.\Publish-Database.ps1

	Publishes the most recently built DACPAC to LocalDB.

.EXAMPLE
	.\Publish-Database.ps1 -ConnectionString "Server=tcp:my-server.database.windows.net,1433;Initial Catalog=CompanioNationDB;Authentication=Active Directory Default;"

	Publishes to Azure SQL using your current Azure identity.
#>

[CmdletBinding()]
param(
	[Parameter(Mandatory = $false)]
	[string]$ConnectionString,

	[Parameter(Mandatory = $false)]
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Release",

	[Parameter(Mandatory = $false)]
	[string]$DacpacPath,

	[Parameter(Mandatory = $false)]
	[switch]$BlockOnPossibleDataLoss,

	[Parameter(Mandatory = $false)]
	[switch]$NonInteractive
)

# Set the console to use the UTF-8 code page
chcp 65001 > $null

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

# Logging function with timestamp
function Log-Message {
	param([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::White)
	$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
	Write-Host "[$timestamp] $Message" -ForegroundColor $Color
}

# Helper function to parse connection string parameters
function Parse-ConnectionString {
	param([string]$connectionString)
	$dict = @{}
	foreach ($part in ($connectionString -split ';')) {
		if ($part -match '=') {
			$key, $value = $part.Split('=', 2)
			$dict[$key.Trim()] = $value.Trim()
		}
	}
	return $dict
}

####################################################################################################
# Constants
####################################################################################################

$localDbConnectionString = "Server=(localdb)\MSSqlLocalDB;Database=CompanioNationDB;Trusted_Connection=True;TrustServerCertificate=True;"

# This script lives next to CompanioNationDB.csproj, so the project directory is
# simply this script's own directory.
$dbProjectDir = $PSScriptRoot
$dbProjectPath = Join-Path $dbProjectDir "CompanioNationDB.csproj"

if (-not (Test-Path $dbProjectPath)) {
	Log-Message "❌ Database project not found at: $dbProjectPath" -Color Red
	Log-Message "   This script must live in the CompanioNationDB project directory." -Color Yellow
	exit 1
}

# Default the target to LocalDB
if (-not $ConnectionString) {
	$ConnectionString = $localDbConnectionString
}

####################################################################################################
# Resolve the DACPAC
####################################################################################################

$dacpac = $null

if ($DacpacPath) {
	if (-not (Test-Path $DacpacPath)) {
		Log-Message "❌ DACPAC not found: $DacpacPath" -Color Red
		exit 1
	}
	$dacpac = (Resolve-Path $DacpacPath).Path
}
else {
	$expectedDacpac = Join-Path $dbProjectDir "bin\$Configuration\net10.0\CompanioNationDB.dacpac"
	if (Test-Path $expectedDacpac) {
		$dacpac = (Resolve-Path $expectedDacpac).Path
	}
	else {
		$fallback = Get-ChildItem -Path (Join-Path $dbProjectDir "bin\$Configuration") -Filter "CompanioNationDB.dacpac" -Recurse -ErrorAction SilentlyContinue |
			Sort-Object LastWriteTime -Descending | Select-Object -First 1
		if ($fallback) {
			$dacpac = $fallback.FullName
		}
		else {
			Log-Message "❌ No DACPAC found under $dbProjectDir\bin\$Configuration." -Color Red
			Log-Message "   Build the CompanioNationDB project first (it produces the DACPAC)." -Color Yellow
			exit 1
		}
	}
	Log-Message "Using DACPAC: $dacpac" -Color Cyan
}

####################################################################################################
# Locate SqlPackage — check PATH, common install paths, then auto-install
####################################################################################################

$sqlPackagePath = (Get-Command SqlPackage -ErrorAction SilentlyContinue).Path
if (-not $sqlPackagePath) {
	$candidates = @(
		"$env:USERPROFILE\.dotnet\tools\SqlPackage.exe",
		"C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
		"C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
		"C:\Program Files\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe"
	)
	foreach ($c in $candidates) { if (Test-Path $c) { $sqlPackagePath = $c; break } }
}
if (-not $sqlPackagePath) {
	Log-Message "⚙️  SqlPackage not found. Installing via dotnet tool (this only happens once)..." -Color Cyan
	try {
		$installOutput = dotnet tool install -g microsoft.sqlpackage 2>&1
		if ($LASTEXITCODE -ne 0 -and ($installOutput -notmatch "already installed")) {
			dotnet tool update -g microsoft.sqlpackage 2>&1 | Out-Null
		}
		$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"
		$sqlPackagePath = (Get-Command SqlPackage -ErrorAction SilentlyContinue).Path
		if (-not $sqlPackagePath) { $sqlPackagePath = "$env:USERPROFILE\.dotnet\tools\SqlPackage.exe" }
		if (-not (Test-Path $sqlPackagePath)) { throw "SqlPackage.exe not found after install." }
		Log-Message "✅ SqlPackage installed successfully." -Color Green
	}
	catch {
		Log-Message "❌ Failed to install SqlPackage: $_" -Color Red
		Log-Message "   Run manually: dotnet tool install -g microsoft.sqlpackage" -Color Yellow
		exit 1
	}
}

Log-Message "Using SqlPackage from: $sqlPackagePath" -Color Cyan

####################################################################################################
# Summary & confirmation
####################################################################################################

$connParams = Parse-ConnectionString $ConnectionString
$server = if ($connParams["Server"]) { $connParams["Server"] } else { $connParams["Data Source"] }
$database = if ($connParams["Initial Catalog"]) { $connParams["Initial Catalog"] } else { $connParams["Database"] }

Log-Message "" -Color White
Log-Message "=====================================================" -Color Yellow
Log-Message "  DATABASE PUBLISH SUMMARY" -Color Yellow
Log-Message "=====================================================" -Color Yellow
Log-Message "  Target Server  : $server" -Color White
Log-Message "  Target Database: $database" -Color White
Log-Message "  DACPAC         : $dacpac" -Color White
Log-Message "  BlockOnDataLoss: $($BlockOnPossibleDataLoss.IsPresent)" -Color White
Log-Message "=====================================================" -Color Yellow

if ($NonInteractive) {
	Log-Message "Non-interactive mode: proceeding without confirmation." -Color Cyan
}
else {
	$confirmation = Read-Host "Publish to database '$database' on server '$server'? [y/N]"
	if ($confirmation -notin @("y", "Y", "yes", "Yes")) {
		Log-Message "Publish cancelled by user." -Color Yellow
		exit 0
	}
}

####################################################################################################
# Publish
####################################################################################################

try {
	Log-Message "Publishing DACPAC to target database..." -Color Cyan

	# AllowIncompatiblePlatform lets an Azure-targeted DACPAC deploy to LocalDB.
	& $sqlPackagePath /Action:Publish /SourceFile:"$dacpac" /TargetConnectionString:"$ConnectionString" `
		/p:BlockOnPossibleDataLoss=$($BlockOnPossibleDataLoss.IsPresent) /p:AllowIncompatiblePlatform=True

	if ($LASTEXITCODE -eq 0) {
		Log-Message "✅ Database published successfully." -Color Green
	}
	else {
		Log-Message "❌ Publish failed with exit code $LASTEXITCODE." -Color Red
		exit $LASTEXITCODE
	}
}
catch {
	Log-Message "❌ Publish failed: $_" -Color Red
	exit 1
}
