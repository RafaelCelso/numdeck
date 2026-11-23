# Builds the native hook DLL with Zig (winget install zig.zig).
# Output: native\bin\NumDeckHook64.dll
# (No 32-bit build needed: Windows calls the 64-bit hook back inside NumDeck for 32-bit apps.)
param([string]$Zig = "zig")

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $here "bin"
New-Item -ItemType Directory -Force $bin | Out-Null

if (-not (Get-Command $Zig -ErrorAction SilentlyContinue)) {
    $found = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter zig.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) { throw "zig nao encontrado. Instale com: winget install zig.zig" }
    $Zig = $found.FullName
}

# Zig fails on cache paths containing spaces; keep its caches elsewhere.
$env:ZIG_GLOBAL_CACHE_DIR = "$env:LOCALAPPDATA\zigcache"
$env:ZIG_LOCAL_CACHE_DIR = "$env:LOCALAPPDATA\zigcache\local"

Push-Location $here
try {
    # Zig's first build of the MinGW CRT is occasionally flaky; retry a few times.
    for ($i = 1; $i -le 4; $i++) {
        & $Zig cc -target x86_64-windows-gnu -shared -O2 -o "$bin\NumDeckHook64.dll" hook.c -luser32
        if ($LASTEXITCODE -eq 0) { break }
        if ($i -eq 4) { throw "Falha ao compilar NumDeckHook64.dll" }
    }
    Get-ChildItem $bin -Include "*.pdb", "*.lib" -Recurse | Remove-Item -Force
    Write-Host "Native hook built in $bin"
}
finally { Pop-Location }
