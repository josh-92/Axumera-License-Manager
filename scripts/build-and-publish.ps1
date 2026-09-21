# Builds and publishes the Axumera License Manager desktop app into
# <repo>\build\Axumera.LicenseManager\ and prints the EXE size + SHA-256.
#
#   & .\scripts\build-and-publish.ps1            (default Release)
#
# Requires the .NET 10 SDK. If `dotnet` is not on PATH it is located under
# %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe (as on this machine).
# The output is framework-dependent: the target machine needs the .NET 10
# Desktop Runtime. The live private_key.pem is NEVER touched or copied here.

$ErrorActionPreference = 'Stop'

# Prefer the SDK installed under %LOCALAPPDATA%\Microsoft\dotnet (where this
# machine keeps the working SDK); fall back to PATH otherwise. Some machines
# expose a broken `dotnet` app-execution alias on PATH that resolves to no SDK.
$localSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localSdk) {
    $dotnetPath = $localSdk
} else {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) { throw 'dotnet SDK not found under %LOCALAPPDATA%\Microsoft\dotnet nor on PATH.' }
    $dotnetPath = $command.Source
}

& $dotnetPath --version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The dotnet at '$dotnetPath' did not respond with an SDK version." }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\Axumera.LicenseManager\Axumera.LicenseManager.csproj'
$out = Join-Path $root 'build\Axumera.LicenseManager'

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }

& $dotnetPath publish $project -c Release -o $out --nologo

$exe = Join-Path $out 'Axumera.LicenseManager.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Publish output did not contain Axumera.LicenseManager.exe.' }

$size = (Get-Item -LiteralPath $exe).Length
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$built = (Get-Item -LiteralPath $exe).LastWriteTime

Write-Host ''
Write-Host "Built:  $exe"
Write-Host "Built at: $built"
Write-Host ("Size:   {0:N0} bytes ({1:N2} KiB)" -f $size, ($size / 1KB))
Write-Host "SHA256: $hash"
Write-Host 'Requires the .NET 10 Desktop Runtime on the target machine.'