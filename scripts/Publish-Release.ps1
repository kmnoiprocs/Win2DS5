param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Notes = "Update.",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+([.]\d+)?$') {
    throw "Use a version like 1.0.1"
}

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

Write-Host "Running tests"
dotnet test (Join-Path $root "tests\Win2DS5.Core.Tests\Win2DS5.Core.Tests.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Publishing Win2DS5 $Version"
$appRelease = Join-Path $root "src\Win2DS5.App"
foreach ($folder in @("bin\Release", "obj\Release")) {
    $path = Join-Path $appRelease $folder
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
dotnet publish (Join-Path $root "src\Win2DS5.App\Win2DS5.App.csproj") -c Release --nologo `
    "-p:Win2DS5Publish=true" `
    "-p:Version=$Version" `
    "-p:SelfContained=true"
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

$publishDir = Join-Path $root "src\Win2DS5.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
if (-not (Test-Path (Join-Path $publishDir "Win2DS5.exe"))) {
    throw "The publish folder does not contain Win2DS5.exe."
}

$stage = Join-Path $artifacts "app"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
& robocopy $publishDir $stage /E /XF *.pdb *.xml /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Could not copy the published files." }

$zip = Join-Path $artifacts "Win2DS5-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
$infoPath = Join-Path $artifacts "release.json"
$json = @{ version = $Version; notes = $Notes; sha256 = $hash } | ConvertTo-Json
[IO.File]::WriteAllText($infoPath, $json.Trim() + "`n", [Text.UTF8Encoding]::new($false))

Write-Host "Building the installer"
$setupBin = Join-Path $root "src\Win2DS5.Setup\bin"
$setupObj = Join-Path $root "src\Win2DS5.Setup\obj"
if (Test-Path $setupBin) { Remove-Item $setupBin -Recurse -Force }
if (Test-Path $setupObj) { Remove-Item $setupObj -Recurse -Force }
dotnet publish (Join-Path $root "src\Win2DS5.Setup\Win2DS5.Setup.csproj") -c Release --nologo `
    "-p:Version=$Version" `
    "-p:PayloadZip=$zip" `
    "-p:SelfContained=true" `
    "-p:PublishSingleFile=true" `
    "-p:IncludeNativeLibrariesForSelfExtract=true" `
    "-p:EnableCompressionInSingleFile=true"
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed." }

$setupBuilt = Join-Path $root "src\Win2DS5.Setup\bin\Release\net10.0-windows\win-x64\publish\Win2DS5-Setup.exe"
if (-not (Test-Path $setupBuilt)) { throw "Win2DS5-Setup.exe was not produced." }
$setupOut = Join-Path $artifacts "Win2DS5-Setup.exe"
Copy-Item $setupBuilt $setupOut -Force
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item $setupOut (Join-Path $dist "Win2DS5-Setup.exe") -Force

if ($Upload) {
    Write-Host "Publishing GitHub release v$Version"
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    gh release view "v$Version" --repo kmnoiprocs/Win2DS5 *> $null
    $exists = $LASTEXITCODE -eq 0
    $ErrorActionPreference = $previous
    if ($exists) { throw "Release v$Version already exists on GitHub." }
    gh release create "v$Version" --repo kmnoiprocs/Win2DS5 --title "Win2DS5 $Version" --notes $Notes $zip $infoPath $setupOut
    if ($LASTEXITCODE -ne 0) { throw "GitHub release upload failed." }
}

Write-Host "Installer ready: $setupOut"
