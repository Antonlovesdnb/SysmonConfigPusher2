# SysmonConfigPusher Installation Guide

This guide covers installing SysmonConfigPusher from the pre-built MSI installer.

> **For developers** building from source, see [DEVELOPMENT.md](DEVELOPMENT.md).

## System Requirements

- Windows Server 2016 or later (domain-joined)
- 4 GB RAM minimum
- 500 MB disk space
- Network access to target endpoints:
  - TCP 135 (WMI/RPC)
  - TCP 445 (SMB)
  - Dynamic RPC ports (49152-65535)

## Quick Install

1. Download `SysmonConfigPusher.msi` from the releases page
2. Run the installer as Administrator
3. Configure a service account for remote management (see Step 4 below)
4. Start the service: `Start-Service SysmonConfigPusher`
5. Access the web UI: https://servername:5001

> **Note:** The service runs as LocalSystem by default, which cannot access remote endpoints. You must configure a domain service account with admin rights on target machines for WMI and SMB access.

## Detailed Installation

### Step 1: Run the Installer

Double-click `SysmonConfigPusher.msi` or run from command line:

```powershell
msiexec /i SysmonConfigPusher.msi /qb
```

The installer will:
- Install files to `C:\Program Files\SysmonConfigPusher`
- Create the data directory at `C:\ProgramData\SysmonConfigPusher`
- Register the Windows Service (set to Manual start, runs as LocalSystem)
- Create firewall rule for port 5001
- Auto-create a self-signed HTTPS certificate on first start

> **Important:** The service runs as LocalSystem by default. This allows the web UI to work, but remote operations (inventory, deployments, event logs) require a domain service account. See Step 4.

### Step 2: Configure TLS Certificate (Optional)

The service auto-creates a self-signed certificate on first start. For production, you may want to use your own certificate:

> **Skip this step** if the self-signed certificate is acceptable (you'll see a browser warning but can proceed).

#### Option A: Windows Certificate Store (Recommended)

1. Import your certificate to the Local Machine store
2. Edit `appsettings.Production.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Subject": "CN=sysmonpusher.yourdomain.com",
          "Store": "My",
          "Location": "LocalMachine"
        }
      }
    }
  }
}
```

3. Grant the service account read access to the certificate private key:
   - Open `certlm.msc`
   - Find your certificate under Personal > Certificates
   - Right-click > All Tasks > Manage Private Keys
   - Add read permission for `LOCAL SERVICE` (or your service account)

#### Option B: PFX File

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Path": "C:\\ProgramData\\SysmonConfigPusher\\cert.pfx",
          "Password": "your-password"
        }
      }
    }
  }
}
```

### Step 3: Configure AD Groups

Create Active Directory groups for role-based access:

| AD Group | Role | Permissions |
|----------|------|-------------|
| SysmonPusher-Admins | Admin | Full access |
| SysmonPusher-Operators | Operator | Deploy, analyze, view |
| SysmonPusher-Viewers | Viewer | Read-only |

Edit `appsettings.Production.json`:

```json
{
  "Authorization": {
    "AdminGroup": "SysmonPusher-Admins",
    "OperatorGroup": "SysmonPusher-Operators",
    "ViewerGroup": "SysmonPusher-Viewers"
  }
}
```

### Step 4: Configure Service Account

The service needs network access to manage remote endpoints via WMI and SMB. By default, it runs as LocalSystem which cannot authenticate to remote machines.

**Requirements:**
- A domain account (regular or gMSA) with local admin rights on target endpoints
- The account must be able to access WMI and admin shares (C$) on remote machines

**Configure the service to use a domain account:**

```powershell
# Stop the service
Stop-Service SysmonConfigPusher

# Configure service account (replace with your account)
$cred = Get-Credential -Message "Enter domain service account credentials"
sc.exe config SysmonConfigPusher obj= $cred.UserName password= $cred.GetNetworkCredential().Password

# Start the service
Start-Service SysmonConfigPusher
```

**Or via Services GUI:**
1. Open `services.msc`
2. Find **Sysmon Config Pusher**
3. Right-click → Properties → **Log On** tab
4. Select "This account" and enter domain credentials
5. Restart the service

> **Tip:** For gMSA setup, refer to Microsoft's documentation on [Group Managed Service Accounts](https://learn.microsoft.com/en-us/windows-server/security/group-managed-service-accounts/group-managed-service-accounts-overview).

### Step 5: Start the Service

```powershell
# Set to auto-start
Set-Service SysmonConfigPusher -StartupType Automatic

# Start the service (if not already started by setup script)
Start-Service SysmonConfigPusher

# Verify status
Get-Service SysmonConfigPusher
```

### Step 6: Access the Web UI

Open a browser and navigate to:
```
https://your-server-name:5001
```

Log in with your domain credentials.

## Configuration Reference

Full `appsettings.Production.json` example:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Subject": "CN=sysmonpusher.yourdomain.com",
          "Store": "My",
          "Location": "LocalMachine"
        }
      }
    }
  },
  "SysmonConfigPusher": {
    "SysmonBinaryUrl": "https://live.sysinternals.com/Sysmon.exe",
    "DefaultParallelism": 50,
    "RemoteDirectory": "C:\\SysmonFiles",
    "AuditLogPath": "C:\\ProgramData\\SysmonConfigPusher\\audit.json",
    "LogDirectory": "C:\\ProgramData\\SysmonConfigPusher\\logs"
  },
  "Authorization": {
    "AdminGroup": "SysmonPusher-Admins",
    "OperatorGroup": "SysmonPusher-Operators",
    "ViewerGroup": "SysmonPusher-Viewers",
    "DefaultRole": "None"
  }
}
```

### Settings Explained

| Setting | Description | Default |
|---------|-------------|---------|
| `SysmonBinaryUrl` | URL to download Sysmon binary | live.sysinternals.com |
| `DefaultParallelism` | Concurrent deployment operations (1-500) | 50 |
| `RemoteDirectory` | Directory on targets for Sysmon files | C:\SysmonFiles |
| `AuditLogPath` | JSON audit log file (empty to disable) | (empty) |
| `LogDirectory` | Application log directory | %ProgramData%\SysmonConfigPusher\logs |

## File Locations

| Path | Contents |
|------|----------|
| `C:\Program Files\SysmonConfigPusher\` | Application files |
| `C:\ProgramData\SysmonConfigPusher\sysmon.db` | SQLite database |
| `C:\ProgramData\SysmonConfigPusher\logs\` | Application logs |
| `C:\ProgramData\SysmonConfigPusher\BinaryCache\` | Cached Sysmon binaries |

## Firewall Configuration

The installer creates a firewall rule automatically. To create manually:

```powershell
New-NetFirewallRule -Name "SysmonConfigPusher" `
    -DisplayName "SysmonConfigPusher Web UI" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 5001 `
    -Action Allow
```

## Database Backup

Use the included backup script:

```powershell
# Manual backup
& "C:\Program Files\SysmonConfigPusher\scripts\backup-database.ps1"

# Schedule daily backups at 2 AM
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" `
    -Argument "-ExecutionPolicy Bypass -File 'C:\Program Files\SysmonConfigPusher\scripts\backup-database.ps1'"
$trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM
Register-ScheduledTask -TaskName "SysmonPusher-Backup" -Action $action -Trigger $trigger
```

## Uninstallation

Use Add/Remove Programs or:

```powershell
msiexec /x SysmonConfigPusher.msi /qb
```

To also remove application data:

```powershell
Remove-Item "C:\ProgramData\SysmonConfigPusher" -Recurse -Force
```

## Troubleshooting

### Service Won't Start

1. Check logs: `C:\ProgramData\SysmonConfigPusher\logs\`
2. Verify certificate is accessible
3. Check Windows Event Log (Application)

### Cannot Connect to Endpoints

Test connectivity:
```powershell
# Test WMI
Get-WmiObject -Class Win32_OperatingSystem -ComputerName TARGET

# Test SMB
Test-Path "\\TARGET\C$"
```

### Authentication Issues

1. Verify AD groups exist and user is a member
2. Clear Kerberos tickets: `klist purge`
3. Check the service logs for authentication errors

### Browser Shows Login Prompt (Windows Authentication)

The application uses Windows Integrated Authentication (Kerberos/NTLM). If your browser shows a login prompt instead of logging in automatically:

**1. Access by hostname, not IP address**

Kerberos authentication requires using the server hostname:
```
https://servername:5001     ✓ Correct
https://192.168.1.50:5001   ✗ Won't auto-authenticate
```

**2. Add site to Local Intranet zone**

Chrome and Edge follow Windows zone settings. Add the site to the Local Intranet zone:

*Via PowerShell (run as Admin):*
```powershell
$serverName = "YOUR-SERVER-NAME"  # Replace with actual hostname
New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap\Domains\$serverName" -Force
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap\Domains\$serverName" -Name "https" -Value 1 -Type DWord
```

*Via Internet Options:*
1. Open **Internet Options** (search in Start menu)
2. Go to **Security** tab → **Local Intranet** → **Sites** → **Advanced**
3. Add: `https://your-server-name:5001`
4. Click Add, Close, OK

**3. Restart the browser**

After changing zone settings, fully close and reopen your browser. You may need to refresh the page a few times for cached credentials to clear.

**4. Chrome Enterprise Policy (optional)**

For organization-wide deployment, configure Chrome via Group Policy or registry:
```powershell
New-Item -Path "HKLM:\SOFTWARE\Policies\Google\Chrome" -Force
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Google\Chrome" -Name "AuthServerAllowlist" -Value "*.yourdomain.com" -Type String
```

### Self-Signed Certificate Warning

The installer creates a self-signed certificate automatically. To suppress browser warnings:

1. When prompted, click **Advanced** → **Proceed to site**
2. Or import the certificate to Trusted Root:
   ```powershell
   # On the client machine, run as Admin
   $cert = Get-ChildItem -Path "\\servername\C$\ProgramData\SysmonConfigPusher\cert.cer"
   Import-Certificate -FilePath "\\servername\cert.cer" -CertStoreLocation Cert:\LocalMachine\Root
   ```

For production, use a certificate from your organization's CA or a trusted provider.
