# Запуск сервера синхронизации для вкладки «Аккаунт».
#
#   powershell -ExecutionPolicy Bypass -File tools/run-sync-server.ps1
#
# Адрес по умолчанию — http://localhost:5080: ровно его приложение предлагает
# на вкладке «Аккаунт», поэтому регистрация заработает без лишних настроек.
# Пока это окно открыто, сервер жив; закроешь — синхронизация снова недоступна,
# а всё остальное в приложении продолжит работать как прежде.

[CmdletBinding()]
param(
    # Адрес, который слушает сервер. Меняй вместе с полем «Адрес сервера» в приложении.
    [string] $Url = "http://localhost:5080",

    # Куда класть базу и ключ подписи токенов. По умолчанию — рядом с сервером.
    [string] $DataDirectory
)

$ErrorActionPreference = "Stop"

# Без этого кириллица в подсказках ниже превращается в вопросительные знаки
# на консоли со старой кодовой страницей.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/GdTracker.Api/GdTracker.Api.csproj"

if (-not (Test-Path $project)) {
    throw "Не нашёл $project — запускай скрипт из папки проекта."
}

if ($DataDirectory) {
    $env:Data__Directory = $DataDirectory
}

Write-Host "Сервер синхронизации: $Url" -ForegroundColor Cyan
Write-Host "Аккаунты и снимки прогресса останутся на этом компьютере." -ForegroundColor DarkGray
Write-Host "Остановить — Ctrl+C." -ForegroundColor DarkGray
Write-Host ""

# --no-launch-profile: иначе адрес взялся бы из launchSettings.json и разошёлся
# бы с тем, что указано здесь и в приложении.
dotnet run --project $project --no-launch-profile --urls $Url
