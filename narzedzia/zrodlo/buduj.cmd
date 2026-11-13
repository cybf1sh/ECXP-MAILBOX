@echo off
rem Buduje ECXP wbudowanym kompilatorem C# z .NET Framework 4:
rem   ..\ecxpd.exe        - stos ECXP (jedyny piszacy, potok nazwany, ramki, straznik)
rem   ..\ECXP-MAILBOX.exe - konsola operatora (klient stosu)
rem   ..\ecxp-mcp.exe     - adapter MCP dla agentow ChatGPT/Codex i Claude (klient stosu)
rem   ..\ECXP-Setup.exe   - instalator stanowiska (PL/EN/FR/CS/DE), zawiera powyzsze programy
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OPT=/nologo /optimize+ /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Management.dll /r:"%FW%\System.Web.Extensions.dll"
set UI=/target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:spis-galezi.html,spis-galezi.html /resource:jezyki.json,jezyki.json
set RDZEN=UsuwanieProjektu.cs Pentest.cs Protokol.cs Siec.cs Ramka.cs Sprawy.cs Agenci.cs Obieg.cs Cieply.cs CodexProjekt.cs CodexBudzenie.cs Tury.cs Aktywnosc.cs
cd /d "%~dp0"
"%CSC%" %OPT% /target:winexe /out:..\ecxpd.exe Ecxpd.cs Doreczanie.cs Widok.cs %RDZEN% || exit /b 1
"%CSC%" %OPT% %UI% /out:..\ECXP-MAILBOX.exe ECXP-MAILBOX.cs Czytnik.cs Szkice.cs Sledzenie.cs Straznik.cs PodgladAgenta.cs SpisGalezi.cs Znaczniki.cs Foldery.cs Galezie.cs Bledy.cs Widok.cs %RDZEN% || exit /b 1
..\ECXP-MAILBOX.exe /ico ..\ECXP-MAILBOX.ico || exit /b 1
"%CSC%" %OPT% %UI% /out:..\ECXP-MAILBOX.exe /win32icon:..\ECXP-MAILBOX.ico ECXP-MAILBOX.cs Czytnik.cs Szkice.cs Sledzenie.cs Straznik.cs PodgladAgenta.cs SpisGalezi.cs Znaczniki.cs Foldery.cs Galezie.cs Bledy.cs Widok.cs %RDZEN% || exit /b 1
"%CSC%" %OPT% /target:exe /out:..\ecxp-mcp.exe Mcp.cs Hooki.cs %RDZEN% || exit /b 1
copy /y hook-claude.ps1 ..\hook-claude.ps1 >nul || exit /b 1
rem instalator stanowiska: jeden plik z programami jako zasobami
"%CSC%" %OPT% /target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:..\ECXP-Setup.exe /win32icon:..\ECXP-MAILBOX.ico ^
  /resource:..\ecxpd.exe,ecxpd.exe /resource:..\ECXP-MAILBOX.exe,ECXP-MAILBOX.exe /resource:..\ecxp-mcp.exe,ecxp-mcp.exe ^
  /resource:..\ECXP-MAILBOX.ico,ECXP-MAILBOX.ico /resource:hook-claude.ps1,hook-claude.ps1 ^
  /resource:..\..\logo\ecxp-logo-light.png,ecxp-logo-light.png /resource:..\..\logo\ecxp-logo-dark.png,ecxp-logo-dark.png Setup.cs || exit /b 1
echo OK
