# ECXP – hook UserPromptSubmit dla Claude Code (lustro 1:1).
# Wpis operatora w czacie sesji Claude należącej do pary ECXP trafia do stosu (stan\wejscia\), a stos przekazuje go
# dosłownie drugiej stronie pary. Sesje spoza ECXP: skrypt nic nie zapisuje. Nie blokuje wpisu i nic nie wypisuje.
# Katalog skrzynki: zmienna ECXP_SKRZYNKA albo katalog nadrzędny wobec tego skryptu (<skrzynka>\narzedzia\hook-claude.ps1).
$ErrorActionPreference = 'SilentlyContinue'
try {
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
    $root = if ($env:ECXP_SKRZYNKA) { $env:ECXP_SKRZYNKA } else { Split-Path -Parent $PSScriptRoot }
    $we = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $sesja = [string]$we.session_id
    $tekst = [string]$we.prompt
    if (-not $sesja -or -not $tekst) { exit 0 }
    $cache = Join-Path $root 'stan\hook-sesje'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $plikC = Join-Path $cache ($sesja + '.txt')
    $zapis = if (Test-Path $plikC) { ([IO.File]::ReadAllText($plikC)).Trim() } else { '' }
    $nazwa = ''
    if ($zapis -match '^claude-[a-z0-9-]+$') { $nazwa = $zapis }
    else {
        # Brak przypisania w pamięci podręcznej: szukamy nazwy w pakiecie startowym albo w wywołaniu ecxp_przypisz.
        # Wynik negatywny sprawdzamy ponownie najwyżej co 5 minut, żeby nie czytać zapisu rozmowy przy każdym wpisie.
        $ostatnio = 0L
        if ($zapis -match '^-\|(\d+)$') { $ostatnio = [long]$Matches[1] }
        if ([DateTime]::UtcNow.Ticks - $ostatnio -gt 3000000000L) {
            $m = [regex]::Match($tekst, 'Twoja nazwa w ECXP: (claude-[a-z0-9-]+)')
            $tp = [string]$we.transcript_path
            if (-not $m.Success -and $tp -and (Test-Path $tp)) {
                $t = [IO.File]::ReadAllText($tp)
                $m = [regex]::Match($t, 'Twoja nazwa w ECXP: (claude-[a-z0-9-]+)')
                if (-not $m.Success) { $m = [regex]::Match($t, 'ecxp_przypisz[\s\S]{0,600}?\\*"ja\\*"\s*:\s*\\*"(claude-[a-z0-9-]+)') }
            }
            if ($m.Success) { $nazwa = $m.Groups[1].Value; [IO.File]::WriteAllText($plikC, $nazwa) }
            else { [IO.File]::WriteAllText($plikC, '-|' + [DateTime]::UtcNow.Ticks) }
        }
    }
    if (-not $nazwa) { exit 0 }
    if ($tekst -match 'Twoja nazwa w ECXP:') { exit 0 }   # pakiet startowy nie jest wpisem dla drugiej strony
    $dir = Join-Path $root 'stan\wejscia'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $o = [ordered]@{ strona = 'claude'; nazwa = $nazwa; sesja = $sesja; tekst = $tekst; czas = (Get-Date).ToString('o') }
    $plik = Join-Path $dir ((Get-Date).ToString('yyyyMMdd-HHmmss-fff') + '-' + $sesja.Substring(0, [Math]::Min(8, $sesja.Length)) + '.json')
    [IO.File]::WriteAllText($plik + '.tmp', ($o | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item ($plik + '.tmp') $plik
} catch { }
exit 0
