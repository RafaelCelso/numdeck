# Builds everything a GitHub release needs into dist\:
#   NumDeck-Setup-<versão>.exe     instalador (Inno Setup)
#   NumDeck-<versão>-portable.zip  versão portátil (só extrair e rodar)
#   SHA256SUMS.txt                 checksums dos arquivos acima
#   release-notes-<versão>.md      texto sugerido para a release
#
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Version 1.0.0
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what falhou (código $LASTEXITCODE)" } }

# Inno Setup compiler (per-user or machine-wide install).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup" }

Push-Location $root
try {
    if (Get-Process NumDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$publish*" }) {
        throw "Feche o NumDeck que está rodando a partir da pasta publish antes de gerar a release."
    }

    if (-not $SkipTests) {
        Step "Testes"
        dotnet test tests\NumDeck.Core.Tests -c Release -nologo -v q
        Check "dotnet test"
    }

    Step "Publicando NumDeck $Version (self-contained, arquivo único)"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish src\NumDeck.App -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none `
        -p:Version=$Version -o $publish -nologo -v q
    Check "dotnet publish"
    foreach ($f in "NumDeck.exe", "native\NumDeckHook64.dll") {
        if (-not (Test-Path (Join-Path $publish $f))) { throw "Arquivo ausente no publish: $f" }
    }

    New-Item -ItemType Directory -Force $dist | Out-Null
    Get-ChildItem $dist -Filter "*$Version*" | Remove-Item -Force

    Step "Gerando instalador"
    & $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$dist" installer\NumDeck.iss
    Check "ISCC"

    Step "Gerando versão portátil"
    $zip = Join-Path $dist "NumDeck-$Version-portable.zip"
    $staging = Join-Path $env:TEMP "numdeck-portable-$Version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force "$staging\NumDeck" | Out-Null
    Copy-Item "$publish\*" "$staging\NumDeck" -Recurse
    Copy-Item README.md "$staging\NumDeck"
    Compress-Archive -Path "$staging\NumDeck" -DestinationPath $zip -Force
    Remove-Item $staging -Recurse -Force

    Step "Checksums e notas da release"
    $assets = @("NumDeck-Setup-$Version.exe", "NumDeck-$Version-portable.zip")
    $sums = foreach ($a in $assets) {
        $h = (Get-FileHash (Join-Path $dist $a) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$h  $a"
    }
    $sums | Set-Content -Encoding ascii (Join-Path $dist "SHA256SUMS.txt")

    # "## <versão>" section of CHANGELOG.md, if present.
    $changes = ""
    if (Test-Path CHANGELOG.md) {
        $log = Get-Content CHANGELOG.md -Raw -Encoding UTF8
        $m = [regex]::Match($log, "(?ms)^## $([regex]::Escape($Version))\s*\r?\n(.*?)(?=^## |\z)")
        if ($m.Success) { $changes = "### Novidades`n" + $m.Groups[1].Value.Trim() + "`n" }
    }

    $notes = @"
## NumDeck $Version

Transforma um teclado numérico externo (USB ou Bluetooth) num Stream Deck para Windows.

$changes
### Download
- **NumDeck-Setup-$Version.exe**: instalador (recomendado). Não precisa de administrador nem do .NET instalado.
- **NumDeck-$Version-portable.zip**: versão portátil. Extraia e rode ``NumDeck.exe`` (mantenha a pasta ``native`` junto).

Requer Windows 10 (1809+) ou 11, 64 bits.

> O Windows SmartScreen pode avisar "o Windows protegeu o computador", porque o instalador não é assinado digitalmente. Clique em **Mais informações → Executar assim mesmo**.

### SHA-256
``````
$($sums -join "`n")
``````
"@
    $notes | Set-Content -Encoding utf8 (Join-Path $dist "release-notes-$Version.md")

    Step "Pronto"
    Get-ChildItem $dist -Filter "*$Version*" | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
    Write-Host "Arquivos em: $dist"
}
finally { Pop-Location }
