$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Önce .env dosyasını oluşturun.' }

$settings = @{}
Get-Content -LiteralPath $envFile | ForEach-Object {
    if ($_ -match '^([^#=]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
foreach ($key in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_PORT')) {
    if (-not $settings.ContainsKey($key)) { throw ".env içinde $key tanımlı değil." }
}
$env:GEOTRACK_TEST_CONNECTION = "Host=localhost;Port=$($settings.POSTGRES_PORT);Database=$($settings.POSTGRES_DB);Username=$($settings.POSTGRES_USER);Password=$($settings.POSTGRES_PASSWORD)"
Push-Location $root
try { dotnet test GeoTrack.sln -p:UseSharedCompilation=false -m:1 --verbosity minimal; exit $LASTEXITCODE }
finally { Pop-Location }
