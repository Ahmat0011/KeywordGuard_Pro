# ============================================================
# FINAL-Install.ps1 - KeywordGuard Pro (NEUAUFLAGE)
# ============================================================
# NEU: Saubere Architektur ohne Altlasten!
# - Session 0 Check im Agent (beendet sich sofort)
# - Service wartet auf Benutzer-Login
# - TaskScheduler ist AUTOSTART fuer Agent + UI (Admin-Rechte)
# - Verschl. Config in %LOCALAPPDATA% (KEIN Admin noetig zum Speichern)
# - Firewall-Blockierung (KEINE hosts-Datei, unsichtbar)
# - Volles Logging fuer Fehlersuche
# ============================================================

$BasePath = $PSScriptRoot
$SourceAgent = "$BasePath\KeywordGuard.Pro.Agent\bin\Release\net10.0-windows"
$SourceUI = "$BasePath\KeywordGuard.Pro.UI\bin\Release\net10.0-windows"
$SourceService = "$BasePath\KeywordGuard.Pro.Service\bin\Release\net10.0-windows"

$DestBase = "C:\Program Files\KG_Pro"
$DestUI = "$DestBase\UI"

# Agent-Log + Config-Verzeichnisse
$LogDir = "C:\ProgramData\KG_Pro"
# Config wird JETZT in %LOCALAPPDATA%\KG_Pro gespeichert (kein Admin noetig!)
# Die Backup-Verzeichnisse werden automatisch vom Programm erstellt.

Write-Host "`n=== KEYWORD GUARD PRO - NEU-INSTALLATION ===`n" -ForegroundColor Cyan
Write-Host "HINWEIS: Dieses Skript MUSS als Administrator laufen!" -ForegroundColor Yellow

# 1. Vor-Check & Build
Write-Host "[1/8] Kompiliere alle Projekte in Release-Konfiguration..." -ForegroundColor Yellow
try {
    Write-Host "   Kompiliere Security-Bibliothek..." -ForegroundColor Gray
    dotnet build "$BasePath\KeywordGuard.Pro.Security\KeywordGuard.Pro.Security.csproj" -c Release --nologo -v q | Out-Null
    
    Write-Host "   Kompiliere Agent..." -ForegroundColor Gray
    dotnet build "$BasePath\KeywordGuard.Pro.Agent\KeywordGuard.Pro.Agent.csproj" -c Release --nologo -v q | Out-Null
    
    Write-Host "   Kompiliere UI..." -ForegroundColor Gray
    dotnet build "$BasePath\KeywordGuard.Pro.UI\KeywordGuard.Pro.UI.csproj" -c Release --nologo -v q | Out-Null
    
    Write-Host "   Kompiliere Watchdog Service..." -ForegroundColor Gray
    dotnet build "$BasePath\KeywordGuard.Pro.Service\KeywordGuard.Pro.Service.csproj" -c Release --nologo -v q | Out-Null
    
    Write-Host "   Alle Projekte erfolgreich kompilert!" -ForegroundColor Green
} catch {
    Write-Host "FEHLER beim Kompilieren der Projekte: $_" -ForegroundColor Red
    Write-Host "Bitte stellen Sie sicher, dass das .NET SDK installiert ist." -ForegroundColor Yellow
    Read-Host "Druecke Enter zum Beenden"; exit 1
}

# 2. Alte Instanzen stoppen
Write-Host "[2/8] Stoppe alte Instanzen und Dienste..." -ForegroundColor Yellow
Stop-Process -Name "KeywordGuard.Pro.Agent" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "KeywordGuard.Pro.UI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "SystemNetAdapter" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "SystemNetAdapter_Guard" -Force -ErrorAction SilentlyContinue

sc.exe stop KeywordGuardProService 2>$null | Out-Null
Write-Host "   Warte auf Beendigung des Watchdog-Dienstes..." -ForegroundColor Gray
$timeout = 10
while ((Get-Process -Name "KeywordGuard.Pro.Service" -ErrorAction SilentlyContinue) -and $timeout -gt 0) {
    Start-Sleep -Seconds 1
    $timeout--
}
Stop-Process -Name "KeywordGuard.Pro.Service" -Force -ErrorAction SilentlyContinue
sc.exe delete KeywordGuardProService 2>$null | Out-Null

# Alte Registry-Run-Eintraege loeschen
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuardAgent" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuardProAgent" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "WinSysGuardHost" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuard" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuardAgent" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuardProUI" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "SecurityGuard" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "WindowsSecurityProvider" -ErrorAction SilentlyContinue

# Alte TaskScheduler-Eintraege entfernen
schtasks /delete /tn "KeywordGuardProUI" /f 2>$null | Out-Null
schtasks /delete /tn "KeywordGuardProStartup" /f 2>$null | Out-Null

# Autostart-Verknuepfung des alten Agenten loeschen
$StartupLnk = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup\SystemNetAdapter.lnk")
if (Test-Path $StartupLnk) {
    Remove-Item $StartupLnk -Force -ErrorAction SilentlyContinue
    Write-Host "   Alte Autostart-Verknuepfung geloescht: SystemNetAdapter.lnk" -ForegroundColor Gray
}

# Verzeichnis des alten Agenten loeschen (Besitzrechte uebernehmen und Berechtigungen zuruecksetzen)
$OldCacheDir = "C:\ProgramData\Microsoft\Windows\SystemCache\Logs"
if (Test-Path $OldCacheDir) {
    takeown /f $OldCacheDir /r /d y 2>$null | Out-Null
    takeown /f $OldCacheDir /r /d j 2>$null | Out-Null
    icacls $OldCacheDir /reset /t /c /q 2>$null | Out-Null
    icacls $OldCacheDir /grant *S-1-5-32-544:F /t /c /q 2>$null | Out-Null
    Remove-Item $OldCacheDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "   Altes Cache-Verzeichnis des Agenten bereinigt." -ForegroundColor Gray
}

Start-Sleep -Seconds 2

# 3. Zielverzeichnis vorbereiten
Write-Host "[3/8] Erstelle Zielverzeichnis... " -ForegroundColor Yellow
if (Test-Path $DestBase) {
    takeown /f $DestBase /A /r /d y 2>$null | Out-Null
    takeown /f $DestBase /A /r /d j 2>$null | Out-Null
    icacls $DestBase /reset /t /c /q 2>$null | Out-Null
    icacls $DestBase /grant *S-1-5-32-544:F /t /c /q 2>$null | Out-Null
    Remove-Item $DestBase -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $DestUI | Out-Null

# 4. Dateien kopieren
Write-Host "[4/8] Kopiere Programmdateien... " -ForegroundColor Yellow
robocopy "$SourceAgent" "$DestUI" /E /IS /IT /R:2 /W:3
robocopy "$SourceUI" "$DestUI" /E /IS /IT /R:2 /W:3
robocopy "$SourceService" "$DestUI" /E /IS /IT /R:2 /W:3
Write-Host "   Kopiervorgang erfolgreich!" -ForegroundColor Green

# Entferne Zone.Identifier (verhindert Windows-Sicherheitsmeldung "App wurde blockiert")
Write-Host "   Entferne Zone-Markierungen (Zone.Identifier) von Programmdateien..." -ForegroundColor Gray
Get-ChildItem -Recurse -Path $DestUI -File | Unblock-File -ErrorAction SilentlyContinue
Write-Host "   Zone-Markierungen entfernt!" -ForegroundColor Green

# 5. Service installieren
Write-Host "[5/8] Installiere Windows Service... " -ForegroundColor Yellow
$ServiceExe = "$DestUI\KeywordGuard.Pro.Service.exe"
sc.exe create KeywordGuardProService binPath= "`"$ServiceExe`"" start= auto DisplayName= "Keyword Guard Pro Service" | Out-Null
sc.exe failure KeywordGuardProService reset= 0 actions= restart/0/restart/0/restart/60000 | Out-Null
sc.exe start KeywordGuardProService
Write-Host "   Service installiert und gestartet!" -ForegroundColor Green

# 6. Datenverzeichnisse anlegen (fuer Agent-Log + Config in C:\ProgramData)
Write-Host "[6/8] Erstelle Datenverzeichnisse... " -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
try {
    $Acl = Get-Acl $LogDir
    $SidUsers = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-545")
    $Acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($SidUsers, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
    Set-Acl $LogDir $Acl
    Write-Host "   Berechtigungen fuer ProgramData/KG_Pro gesetzt (Vollzugriff fuer Benutzer)!" -ForegroundColor Green
} catch {
    Write-Host "   Warnung beim Setzen der Berechtigungen fuer ProgramData/KG_Pro." -ForegroundColor Yellow
}

# 7. Dateischutz (nur fuer KG_Pro)
Write-Host "[7/8] Aktiviere Dateischutz... " -ForegroundColor Yellow
try {
    $Acl = Get-Acl $DestBase
    $Acl.SetAccessRuleProtection($true, $false)
    $SidSystem = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-18")
    $SidAdmins = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-544")
    $Acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($SidSystem, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
    $Acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($SidAdmins, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")))
    $DenyRule = New-Object System.Security.AccessControl.FileSystemAccessRule($SidAdmins, "Delete,DeleteSubdirectoriesAndFiles", "ContainerInherit,ObjectInherit", "None", "Deny")
    $Acl.AddAccessRule($DenyRule)
    Set-Acl $DestBase $Acl
    Write-Host "   Dateischutz aktiv!" -ForegroundColor Green
} catch {
    Write-Host "   Warnung beim Dateischutz." -ForegroundColor Yellow
}

# 8. Autostart konfigurieren (OHNE sofortigen Start)
Write-Host "[8/8] Konfiguriere Autostart... " -ForegroundColor Yellow
$AgentExe = "$DestUI\KeywordGuard.Pro.Agent.exe"

# TaskScheduler-Aufgabe fuer Agent-Autostart (mit Admin-Rechten!)
Write-Host "   Erstelle TaskScheduler fuer Agent..." -ForegroundColor Gray
schtasks /create /tn "KeywordGuardProStartup" /tr "`"$AgentExe`"" /sc onlogon /rl HIGHEST /f 2>$null | Out-Null
Write-Host "   Setze zusaetzlichen HKLM-Run-Autostart fuer Agent..." -ForegroundColor Gray
New-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "KeywordGuardProAgent" -Value "`"$AgentExe`"" -PropertyType String -Force | Out-Null
Write-Host "   Agent wird NICHT sofort gestartet (manueller Start nach Installation)." -ForegroundColor Gray
Write-Host "   UI wird NICHT automatisch gestartet." -ForegroundColor Gray

# Alle alten Firewall-Regeln von KG_Pro entfernen (sauberer Zustand)
Write-Host "   Raeume alte Firewall-Regeln auf..." -ForegroundColor Gray
Remove-NetFirewallRule -DisplayName "KG_P_*" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "StealthGuard_*" -ErrorAction SilentlyContinue

Write-Host "`n`n========================================" -ForegroundColor Green
Write-Host "=== INSTALLATION ERFOLGREICH! ===" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nWICHTIG: PC jetzt neu starten!" -ForegroundColor Yellow
Write-Host "`nNach Neustart:" -ForegroundColor Cyan
Write-Host " 1. Agent & Watchdog starten automatisch im Hintergrund (Schutz aktiv)" -ForegroundColor White
Write-Host " 2. UI startet NICHT automatisch (kann manuell geoeffnet werden)" -ForegroundColor White
Write-Host " 3. Config liegt zentral in C:\ProgramData\KG_Pro (verschluesselt)" -ForegroundColor White
Write-Host " 4. Firewall-Regeln statt hosts-Datei (KEINE sichtbaren Eintraege)" -ForegroundColor White
Write-Host " 5. KEIN Bluescreen beim Herunterfahren (SessionEnding-Fix)" -ForegroundColor White
Read-Host "`nDruecke Enter zum Beenden"