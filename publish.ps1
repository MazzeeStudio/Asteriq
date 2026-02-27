# Asteriq publish script
# Builds a self-contained single-file release and packages it as Asteriq.zip.
# The fixed ZIP name (Asteriq.zip) allows GitHub's releases/latest/download/Asteriq.zip
# URL to always point to the latest release.
# Usage: .\publish.ps1

$ErrorActionPreference = "Stop"

$csproj    = "src\Asteriq\Asteriq.csproj"
$outputDir = "publish-release"
$distDir   = "dist"
$zipPath   = Join-Path (Get-Location) "$distDir\Asteriq.zip"

# --- Read version ---
[xml]$proj = Get-Content $csproj
$major   = $proj.Project.PropertyGroup.MajorVersion | Where-Object { $_ } | Select-Object -First 1
$minor   = $proj.Project.PropertyGroup.MinorVersion | Where-Object { $_ } | Select-Object -First 1
$build   = git rev-list --count HEAD
$version = "$major.$minor.$build"

Write-Host "Building Asteriq v$version..."

# --- Publish (self-contained: no .NET install required by users) ---
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $outputDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

# --- Remove debug symbols (not needed by users) ---
Get-ChildItem $outputDir -Filter "*.pdb" | Remove-Item -Force

# --- Package ---
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath }

Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath

Write-Host ""
Write-Host "Done: dist\Asteriq.zip  (v$version)"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  git push"
Write-Host "  gh release create v$version dist\Asteriq.zip --title `"Asteriq v$version`" --notes `"<release notes>`""
