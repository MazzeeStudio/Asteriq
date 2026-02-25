# Asteriq publish script
# Builds a Release single-file executable and zips it with the version in the filename.
# Usage: .\publish.ps1

$ErrorActionPreference = "Stop"

$csproj = "src\Asteriq\Asteriq.csproj"
$outputDir = "publish"

# --- Read version from csproj ---
[xml]$proj = Get-Content $csproj
$major = $proj.Project.PropertyGroup.MajorVersion | Where-Object { $_ } | Select-Object -First 1
$minor = $proj.Project.PropertyGroup.MinorVersion | Where-Object { $_ } | Select-Object -First 1
$build = git rev-list --count HEAD
$version = "$major.$minor.$build"

Write-Host "Building Asteriq v$version..."

# --- Publish ---
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# --- Zip ---
$distDir = "dist"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

$zipName = "Asteriq-v$version.zip"
$zipPath = Join-Path (Join-Path (Get-Location) $distDir) $zipName

if (Test-Path $zipPath) { Remove-Item $zipPath }

Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath

Write-Host "Done: $distDir\$zipName"
