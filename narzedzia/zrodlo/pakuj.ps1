param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
Push-Location $PSScriptRoot
try {
    & .\buduj.cmd
    if($LASTEXITCODE -ne 0){throw 'Kompilacja nie powiodła się.'}
    $bin=Join-Path $Output 'narzedzia'
    $logo=Join-Path $Output 'logo'
    New-Item -ItemType Directory -Path $bin,$logo -Force | Out-Null
    foreach($name in @('ECXP-MAILBOX.exe','ecxpd.exe','ecxp-mcp.exe','ECXP-Setup.exe','ECXP-MAILBOX.ico','hook-claude.ps1')) {
        Copy-Item -LiteralPath (Join-Path '..' $name) -Destination (Join-Path $bin $name)
    }
    foreach($name in @('ecxp-logo-light.png','ecxp-logo-dark.png')) {
        Copy-Item -LiteralPath (Join-Path '..\..\logo' $name) -Destination (Join-Path $logo $name)
    }
    $hashes=Get-ChildItem -LiteralPath $bin -File | ForEach-Object {
        [pscustomobject]@{file=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash;version=$_.VersionInfo.FileVersion}
    }
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'sha256.json') -Encoding UTF8
    $assembly=[Reflection.Assembly]::LoadFile((Join-Path $bin 'ECXP-Setup.exe'))
    $checks=foreach($name in $assembly.GetManifestResourceNames()) {
        $stream=$assembly.GetManifestResourceStream($name)
        $sha=[Security.Cryptography.SHA256]::Create()
        try {
            $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')
            $file=if($name.EndsWith('.png')){Join-Path $logo $name}else{Join-Path $bin $name}
            [pscustomobject]@{resource=$name;matches=($hash -eq (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)}
        } finally {$stream.Dispose();$sha.Dispose()}
    }
    $checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'installer-resources.json') -Encoding UTF8
    if($checks.Count -ne 7 -or @($checks | Where-Object {-not $_.matches}).Count){throw 'Niezgodne zasoby instalatora.'}
    Write-Output ('Pakiet gotowy; zasoby instalatora 7/7: '+$Output)
} finally {Pop-Location}
