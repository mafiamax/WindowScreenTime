# CHANGELOG.md icinden verilen surumun bolumunu cikarir ve yayin notu dosyasina yazar.
# Ayrica proje dosyalarindaki <Version> degerinin etiketle ayni oldugunu dogrular.
# Bolum yoksa, bossa veya surumler uyusmuyorsa hata verir ve yayini durdurur.
#
# Kullanim:
#   pwsh .github/scripts/extract-release-notes.ps1 -Tag v1.2.0 -OutFile release_notes.md
#
# Not: Bu dosya bilerek yalnizca ASCII karakter icerir (Windows PowerShell 5.1 uyumlulugu).

param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$ChangelogPath = "CHANGELOG.md",
    [string]$OutFile = "release_notes.md",
    [string[]]$ProjectFiles = @("WindowScreenTime.csproj", "Installer/WindowScreenTime.Setup.csproj")
)

$ErrorActionPreference = "Stop"

$version = $Tag.Trim()
if ($version.StartsWith("refs/tags/")) { $version = $version.Substring("refs/tags/".Length) }
$version = $version.TrimStart("v", "V")

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Etiket 'vX.Y.Z' biciminde olmali (gelen: '$Tag')."
}

# 1) Proje dosyalarindaki <Version> etiketle ayni mi?
foreach ($proj in $ProjectFiles) {
    if (-not (Test-Path $proj)) { throw "Proje dosyasi bulunamadi: $proj" }
    [xml]$xml = Get-Content -Path $proj -Raw -Encoding UTF8
    $projVersion = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if ("$projVersion".Trim() -ne $version) {
        throw "$proj icindeki <Version> ($projVersion) etiketle ($version) ayni degil. Once surumu guncelleyin."
    }
}

# 2) CHANGELOG.md icinden surum bolumunu bul
if (-not (Test-Path $ChangelogPath)) { throw "$ChangelogPath bulunamadi." }
$lines = Get-Content -Path $ChangelogPath -Encoding UTF8

$headerPattern = '^##\s+\S+\s+v?' + [regex]::Escape($version) + '(\s|\(|$)'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $headerPattern) { $start = $i; break }
}
if ($start -lt 0) {
    throw "$ChangelogPath icinde '## Surum $version' basligi yok. Yayindan once bu surumun notlarini ekleyin."
}

$body = New-Object System.Collections.Generic.List[string]
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s' -or $lines[$i] -match '^---\s*$') { break }
    $body.Add($lines[$i])
}

# Bastaki ve sondaki bos satirlari kirp
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) { $body.RemoveAt(0) }
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) { $body.RemoveAt($body.Count - 1) }

if ($body.Count -eq 0) {
    throw "$ChangelogPath icindeki '$version' bolumu bos."
}

$text = ($body -join "`n") + "`n"
$outPath = if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { Join-Path (Get-Location) $OutFile }
[System.IO.File]::WriteAllText($outPath, $text, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Yayin notu hazir ($version, $($body.Count) satir) -> $OutFile"
Write-Host $text
