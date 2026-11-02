# ECXP – hook UserPromptSubmit dla Claude Code (lustro 1:1 i doręczanie).
# Wpis operatora w czacie sesji Claude należącej do pary ECXP trafia do stosu (stan\wejscia\), a stos przekazuje go
# dosłownie drugiej stronie pary. Gdy na tę sesję czekają nieodebrane wiadomości ECXP, hook dopisuje do kontekstu
# przypomnienie, żeby sesja je odebrała. Sesje spoza ECXP: skrypt nic nie zapisuje i nic nie wypisuje. Nie blokuje wpisu.
# Katalog skrzynki: zmienna ECXP_SKRZYNKA albo katalog nadrzędny wobec tego skryptu (<skrzynka>\narzedzia\hook-claude.ps1).
$ErrorActionPreference = 'SilentlyContinue'
# tura budzenia uruchomiona przez stos (Doreczanie.cs) – to nie wpis operatora: bez lustra i bez przypomnienia
if ($env:ECXP_DORECZANIE) { exit 0 }
try {
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $root = if ($env:ECXP_SKRZYNKA) { $env:ECXP_SKRZYNKA } else { Split-Path -Parent $PSScriptRoot }
    $we = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $sesja = [string]$we.session_id
    $tekst = [string]$we.prompt
    if (-not $sesja -or -not $tekst) { exit 0 }
    # zdarzenia systemowe (powiadomienia o zadaniach w tle, przypomnienia) to nie wpisy operatora
    if ($tekst -match '^\s*<(task-notification|system-reminder|local-command|command-name|command-message|bash-input|bash-stdout|user-prompt-submit-hook)') { exit 0 }
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
                # tylko pakiet wklejony przez użytkownika (wpis użytkownika, nie wynik narzędzia) albo własne wywołanie
                # ecxp_przypisz – sesja, która tylko wyświetliła cudzy pakiet, nie staje się stroną pary
                foreach ($l in [IO.File]::ReadLines($tp)) {
                    if ($l -match '"type":"user"' -and $l -notmatch '"tool_use_id"') {
                        $m = [regex]::Match($l, 'Twoja nazwa w ECXP: (claude-[a-z0-9-]+)'); if ($m.Success) { break }
                    }
                    elseif ($l -match '"type":"assistant"' -and $l -match '"name":"mcp__ecxp__ecxp_przypisz"') {
                        $m = [regex]::Match($l, '"name":"mcp__ecxp__ecxp_przypisz"[^}]*?"ja":"(claude-[a-z0-9-]+)"'); if ($m.Success) { break }
                    }
                }
            }
            if ($m.Success) { $nazwa = $m.Groups[1].Value; [IO.File]::WriteAllText($plikC, $nazwa) }
            else { [IO.File]::WriteAllText($plikC, '-|' + [DateTime]::UtcNow.Ticks) }
        }
    }
    if (-not $nazwa) { exit 0 }
    # nieodebrane wiadomości dla tej sesji: przypomnienie trafia do kontekstu modelu (wyjście hooka UserPromptSubmit)
    try {
        $czeka = @(Get-ChildItem (Join-Path $root 'do-claude') -Filter '*.json' -File | ForEach-Object {
            try { $j = [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json; if ([string]$j.do -eq $nazwa) { [string]$j.temat } } catch { } })
        if ($czeka.Count -gt 0) {
            Write-Output ("[ECXP] Na adres $nazwa czeka nieodebranych wiadomości: " + $czeka.Count + " (" + (($czeka | Select-Object -First 6) -join ', ') +
                "). Odbierz je narzędziem ecxp_odbierz(ja=$nazwa) i potwierdź każdą – zanim zajmiesz się resztą.")
        }
    } catch { }
    if ($tekst -match 'Twoja nazwa w ECXP:') { exit 0 }   # pakiet startowy nie jest wpisem dla drugiej strony
    $dir = Join-Path $root 'stan\wejscia'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $o = [ordered]@{ strona = 'claude'; nazwa = $nazwa; sesja = $sesja; tekst = $tekst; czas = (Get-Date).ToString('o') }
    $plik = Join-Path $dir ((Get-Date).ToString('yyyyMMdd-HHmmss-fff') + '-' + $sesja.Substring(0, [Math]::Min(8, $sesja.Length)) + '.json')
    [IO.File]::WriteAllText($plik + '.tmp', ($o | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))
    Move-Item ($plik + '.tmp') $plik
} catch { }
exit 0
