param([Parameter(Mandatory=$true)][string]$BackupFile)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fullBackup = (Resolve-Path -LiteralPath $BackupFile).Path
if (-not (Test-Path -LiteralPath $fullBackup -PathType Leaf)) { throw 'Yedek dosyası bulunamadı.' }
Push-Location $root
try {
    $database = 'geotrack_restore_' + (Get-Date -Format 'yyyyMMddHHmmss') + '_' + [guid]::NewGuid().ToString('N').Substring(0,6)
    $containerFile = '/tmp/restore-' + [guid]::NewGuid().ToString('N') + '.dump'
    docker cp $fullBackup "geotrack-db:$containerFile"
    if ($LASTEXITCODE -ne 0) { throw 'Yedek dosyası veritabanı kapsayıcısına kopyalanamadı.' }
    $command = 'createdb -U "$POSTGRES_USER" ' + $database + ' && pg_restore -U "$POSTGRES_USER" -d ' + $database + ' --no-owner --no-privileges ' + $containerFile + ' && psql -U "$POSTGRES_USER" -d ' + $database + ' -Atc "SELECT count(*) FROM incidents"'
    docker compose exec -T db sh -c $command
    if ($LASTEXITCODE -ne 0) { throw "Geri yükleme başarısız oldu. Yeni veritabanı: $database; kaynak veritabanı değiştirilmedi." }
    docker compose exec -T db rm -f $containerFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Geri yüklenen veritabanı $database olarak hazır; kapsayıcıda geçici yedek dosyası kaldı." }
    "Yedek yeni veritabanına yüklendi: $database. Mevcut veritabanına dokunulmadı."
}
finally { Pop-Location }
