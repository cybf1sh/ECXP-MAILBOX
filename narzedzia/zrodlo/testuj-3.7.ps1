param([string]$Stack='',[string]$Output='',
    [ValidateSet('TestOpcjiPentestu','TestUsuwaniaProjektu','TestNapiszOdpowiedz','TestOdmow','TestPentestu','TestOdpowiedziOperatora','TestDoreczania','TestStartuPentestu','TestStraznika','TestAktywnosci','TestProtokolu','TestNiezawodnosci','TestKatalogu','TestKonfiguracji','TestyRozmow','TestObserwatorium','TestHookow')]
    [string[]]$Tests=@('TestOpcjiPentestu','TestUsuwaniaProjektu','TestNapiszOdpowiedz','TestOdmow','TestPentestu','TestOdpowiedziOperatora','TestDoreczania','TestStartuPentestu','TestStraznika','TestAktywnosci','TestProtokolu','TestNiezawodnosci','TestKatalogu','TestKonfiguracji','TestyRozmow','TestObserwatorium','TestHookow'))
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    if(-not $Stack){$Stack=Join-Path $repo 'builds\release-3.7.0\narzedzia\ecxpd.exe'}
    $Stack=[IO.Path]::GetFullPath($Stack)
    if(-not $Output){$Output=Join-Path $repo ('builds\regresja-3.7.0-'+[guid]::NewGuid().ToString('N'))}
    if(Test-Path -LiteralPath $Output){throw 'Katalog wynikow musi byc nowy.'}
    New-Item -ItemType Directory -Path $Output | Out-Null
    $fw=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler=Join-Path $fw 'csc.exe'
    $sources=@('ECXP-MAILBOX.cs','Hooki.cs','AnalizaObserwatorium.cs','TechnikiObserwatorium.cs','ProfileObserwatorium.cs','OknoUstawien.cs','Konfiguracja.cs','Czytnik.cs','Szkice.cs','Sledzenie.cs','Straznik.cs','PodgladAgenta.cs','SpisGalezi.cs','Znaczniki.cs','Foldery.cs','Galezie.cs','KatalogAgentow.cs','OknoKatalogu.cs','Osoby.cs','Bledy.cs','Widok.cs','UsuwanieProjektu.cs','Pentest.cs','Protokol.cs','Siec.cs','Ramka.cs','Sprawy.cs','Agenci.cs','Obieg.cs','Cieply.cs','CodexProjekt.cs','CodexBudzenie.cs','Tury.cs','Aktywnosc.cs','Niezawodnosc.cs','Wylaczeni.cs','Profile.cs')
    $options=@('/nologo','/optimize+','/codepage:65001','/target:exe','/r:System.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.Management.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',('/r:'+(Join-Path $fw 'System.Web.Extensions.dll')),'/resource:spis-galezi.html,spis-galezi.html','/resource:jezyki.json,jezyki.json','/resource:obserwatorium-interakcje.html,obserwatorium-interakcje.html')
    foreach($binary in @('ECXP-MAILBOX.exe','ecxpd.exe','ecxp-mcp.exe')) { Copy-Item -LiteralPath (Join-Path (Split-Path $Stack -Parent) $binary) -Destination $Output }
    $results=@()
    foreach($name in $Tests){
        $exe=Join-Path $Output ($name+'.exe')
        & $compiler @options ('/main:Ecxp.'+$name) ('/out:'+$exe) @sources ($name+'.cs') *> (Join-Path $Output ($name+'-build.txt'))
        if($LASTEXITCODE -ne 0){throw ('Kompilacja: '+$name)}
        $testRoot=Join-Path $Output ($name+'-'+[guid]::NewGuid().ToString('N'))
        $arguments=@($testRoot)
        if($name -eq 'TestOpcjiPentestu'){$arguments+=@($Stack,(Join-Path $PSScriptRoot 'FixtureOpcji.py'))}
        if($name -eq 'TestUsuwaniaProjektu'){$arguments+=(Join-Path $PSScriptRoot 'FixtureUsuwania.py')}
        if($name -eq 'TestNapiszOdpowiedz'){$arguments+=(Join-Path $PSScriptRoot 'FixtureWiadomosci.py')}
        $log=Join-Path $Output ($name+'.txt')
        & $exe @arguments *> $log
        $code=$LASTEXITCODE
        $lines=Get-Content -LiteralPath $log
        $row=[pscustomobject]@{Test=$name;Exit=$code;PASS=@($lines | Select-String '^PASS').Count;FAIL=@($lines | Select-String '^FAIL').Count}
        $results+=$row
        $row | ConvertTo-Json -Compress
        if($code -ne 0 -or $row.FAIL -gt 0){throw ('Test nie przeszedl: '+$name+'; '+$log)}
    }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'wyniki.json') -Encoding UTF8
    Write-Output ('Wyniki: '+$Output)
} finally {Pop-Location}
