param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 if(Test-Path -LiteralPath $Output){throw 'Output must be new'}
 $bin=Join-Path $Output 'pakiet przenośny Żółć\narzedzia'
 New-Item -ItemType Directory -Path $bin | Out-Null
 $fw=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
 $compiler=Join-Path $fw 'csc.exe'
    $sources=@('ECXP-MAILBOX.cs','OknoUstawien.cs','Konfiguracja.cs','Czytnik.cs','Szkice.cs','Sledzenie.cs','Straznik.cs','PodgladAgenta.cs','SpisGalezi.cs','Znaczniki.cs','Foldery.cs','Galezie.cs','KatalogAgentow.cs','OknoKatalogu.cs','Osoby.cs','Bledy.cs','Widok.cs','UsuwanieProjektu.cs','Pentest.cs','Protokol.cs','Siec.cs','Ramka.cs','Sprawy.cs','Agenci.cs','Obieg.cs','Cieply.cs','CodexProjekt.cs','CodexBudzenie.cs','Tury.cs','Aktywnosc.cs','Niezawodnosc.cs','Wylaczeni.cs','Profile.cs')
    $options=@('/nologo','/optimize+','/codepage:65001','/target:exe','/r:System.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.Management.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',('/r:'+(Join-Path $fw 'System.Web.Extensions.dll')),'/resource:spis-galezi.html,spis-galezi.html','/resource:jezyki.json,jezyki.json')
 & $compiler @options '/main:Ecxp.TestMigracji' ('/out:'+(Join-Path $bin 'ECXP-MAILBOX.exe')) @sources TestMigracji.cs *> (Join-Path $Output 'build.txt')
 if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
 Copy-Item -LiteralPath '..\ecxpd.exe','..\ecxp-mcp.exe' -Destination $bin
 & (Join-Path $bin 'ECXP-MAILBOX.exe') (Join-Path $Output 'źródłowa skrzynka') (Join-Path $Output 'nowa skrzynka Żółć') *> (Join-Path $Output 'wynik.txt')
 Get-Content -LiteralPath (Join-Path $Output 'wynik.txt')
 if($LASTEXITCODE -ne 0){throw 'Native migration test failed'}
} finally {Pop-Location}
