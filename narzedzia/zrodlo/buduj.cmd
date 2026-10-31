@echo off
rem Buduje ECXP wbudowanym kompilatorem C# z .NET Framework 4:
rem   ..\ecxpd.exe        - stos ECXP (jedyny piszacy, potok nazwany, ramki, straznik)
rem   ..\ECXP-MAILBOX.exe - konsola operatora (klient stosu)
rem   ..\ecxp-mcp.exe     - adapter MCP dla agentow ChatGPT/Codex i Claude (klient stosu)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OPT=/nologo /optimize+ /codepage:65001 /r:System.dll /r:System.Core.dll /r:"%FW%\System.Web.Extensions.dll"
set UI=/target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll
set RDZEN=Protokol.cs Siec.cs Ramka.cs
cd /d "%~dp0"
"%CSC%" %OPT% /target:winexe /out:..\ecxpd.exe Ecxpd.cs %RDZEN% || exit /b 1
"%CSC%" %OPT% %UI% /out:..\ECXP-MAILBOX.exe ECXP-MAILBOX.cs %RDZEN% || exit /b 1
..\ECXP-MAILBOX.exe /ico ..\ECXP-MAILBOX.ico || exit /b 1
"%CSC%" %OPT% %UI% /out:..\ECXP-MAILBOX.exe /win32icon:..\ECXP-MAILBOX.ico ECXP-MAILBOX.cs %RDZEN% || exit /b 1
"%CSC%" %OPT% /target:exe /out:..\ecxp-mcp.exe Mcp.cs %RDZEN% || exit /b 1
echo OK
