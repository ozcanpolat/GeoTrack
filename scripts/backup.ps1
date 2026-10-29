param([string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'backups'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $runningServices = @(docker compose ps --status running --services)
    if ($LASTEXITCODE -ne 0 -or $runningServices -notcontains 'db') { throw 'PostGIS container çalışmıyor.' }
    $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
    $destination = Join-Path (Resolve-Path $OutputDirectory).Path ("geotrack-{0}-{1}.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [guid]::NewGuid().ToString('N').Substring(0,8))
    $containerFile = '/tmp/' + [IO.Path]::GetFileName($destination)
    $command = 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f ' + $containerFile + ' && pg_restore --list ' + $containerFile + ' >/dev/null'
    docker compose exec -T db sh -c $command
    if ($LASTEXITCODE -ne 0) { throw 'Veritabanı yedeği oluşturulamadı veya doğrulanamadı.' }
    docker cp "geotrack-db:$containerFile" $destination
    if ($LASTEXITCODE -ne 0) { throw 'Yedek dosyası host makineye kopyalanamadı.' }
    docker compose exec -T db rm -f $containerFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Kapsayıcıdaki geçici yedek kaldırılamadı; yedek hosta kopyalandı.' }
    "Yedek oluşturuldu ve pg_restore --list ile doğrulandı: $destination"
}
finally { Pop-Location }
