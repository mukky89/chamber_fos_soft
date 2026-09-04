$ErrorActionPreference = 'Stop'

$crlfExtensions = @('.cs', '.csproj', '.xaml', '.sln', '.slnlaunch', '.md', '.ps1')
$lfExtensions = @('.sh', '.yml', '.yaml', '.json', '.xml')
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$updated = 0

foreach ($relativePath in (& git ls-files)) {
    $extension = [System.IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    if ($extension -notin $crlfExtensions -and $extension -notin $lfExtensions) {
        continue
    }

    $fullPath = Join-Path $PSScriptRoot "..\$relativePath"
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) {
        $text = $text.TrimStart([char]0xFEFF)
    }

    $normalized = $text -replace "`r`n", "`n" -replace "`r", "`n"
    if ($extension -in $crlfExtensions) {
        $normalized = $normalized -replace "`n", "`r`n"
    }

    $newBytes = $utf8NoBom.GetBytes($normalized)
    if ($hasBom) {
        $newBytes = [byte[]](0xEF, 0xBB, 0xBF) + $newBytes
    }
    $contentChanged = $bytes.Length -ne $newBytes.Length -or
        [Convert]::ToBase64String($bytes) -ne [Convert]::ToBase64String($newBytes)
    if ($contentChanged) {
        [System.IO.File]::WriteAllBytes($fullPath, $newBytes)
        $updated++
    }
}

Write-Output "Normalized line endings in $updated tracked files."
