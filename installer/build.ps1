<#
    Сборка инсталлятора GD Progress Tracker.

    Публикует приложение как self-contained win-x64 и компилирует installer\GdTracker.iss
    в installer\output\GdTrackerSetup-<версия>.exe.

    Версия берётся из Directory.Build.props — единственного места, где она задана.

    Пример:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
[CmdletBinding()]
param(
    # Готовый каталог публикации: если передан, шаг dotnet publish пропускается.
    [string] $PublishDir,
    # Куда положить собранный setup.exe.
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $root 'artifacts\publish' }
if (-not $OutputDir)  { $OutputDir  = Join-Path $PSScriptRoot 'output' }

# --- версия ---------------------------------------------------------------
$propsPath = Join-Path $root 'Directory.Build.props'
$version = ([xml](Get-Content -Raw -Encoding UTF8 $propsPath)).Project.PropertyGroup.Version
if (-not $version) { throw "Не удалось прочитать <Version> из $propsPath" }
$version = $version.Trim()
Write-Host "Версия: $version"

# --- ISCC -----------------------------------------------------------------
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe не найден. Установите Inno Setup 6: winget install --id JRSoftware.InnoSetup -e"
}

# --- публикация -----------------------------------------------------------
if ($PSBoundParameters.ContainsKey('PublishDir')) {
    Write-Host "Использую готовую публикацию: $PublishDir"
} else {
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    Write-Host "Публикую приложение в $PublishDir"
    # SatelliteResourceLanguages=en выкидывает переводы зависимостей — минус ~15 МБ,
    # у самого приложения сателлитных сборок нет.
    & dotnet publish (Join-Path $root 'src\GdTracker.App\GdTracker.App.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:SatelliteResourceLanguages=en `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }
}

$exePath = Join-Path $PublishDir 'GdTracker.App.exe'
if (-not (Test-Path $exePath)) { throw "В публикации нет GdTracker.App.exe: $PublishDir" }

# --- компиляция инсталлятора ---------------------------------------------
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Write-Host "Собираю инсталлятор через $iscc"
& $iscc "/DAppVersion=$version" "/DPublishDir=$PublishDir" "/DOutputDir=$OutputDir" `
        (Join-Path $PSScriptRoot 'GdTracker.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с кодом $LASTEXITCODE" }

$setup = Join-Path $OutputDir "GdTrackerSetup-$version.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "Готово: $setup ($sizeMb МБ)"
