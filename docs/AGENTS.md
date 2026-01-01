# SysmonConfigPusher Agent Guide

This guide covers the lightweight agent for managing Sysmon on cloud-hosted Windows machines that cannot be reached via WMI/SMB.

## Table of Contents

- [Overview](#overview)
- [When to Use Agents](#when-to-use-agents)
- [Architecture](#architecture)
- [Installation](#installation)
- [Configuration](#configuration)
- [Agent Management](#agent-management)
- [Troubleshooting](#troubleshooting)

---

## Overview

The SysmonConfigPusher Agent is a lightweight Windows service that enables Sysmon management on machines that cannot be accessed via traditional WMI/SMB methods. This is ideal for:

- Cloud-hosted VMs (Azure, AWS, GCP)
- Machines behind NAT or firewalls
- DMZ servers
- Workgroup machines (not domain-joined)

### How It Works

```
┌─────────────────────────────────────────────────────────────────────┐
│                 SysmonConfigPusher Server                            │
│                     (Your Management Host)                           │
└─────────────────────────────┬───────────────────────────────────────┘
                              │ HTTPS (outbound only)
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐     ┌───────────────┐     ┌───────────────┐
│   Agent VM    │     │   Agent VM    │     │   Agent VM    │
│  (Cloud/DMZ)  │     │  (Cloud/DMZ)  │     │  (Cloud/DMZ)  │
│               │     │               │     │               │
│  Polls server │     │  Polls server │     │  Polls server │
│  for commands │     │  for commands │     │  for commands │
└───────────────┘     └───────────────┘     └───────────────┘
```

**Key characteristics:**
- Agent initiates all connections (outbound HTTPS only)
- No inbound firewall rules required on agent machines
- Token-based authentication (no Windows auth dependency)
- Lightweight (~15MB self-contained executable)
- Runs as a Windows service (SYSTEM account)

---

## When to Use Agents

| Scenario | Use Agent? | Notes |
|----------|------------|-------|
| Domain-joined workstations | No | Use standard WMI/SMB |
| Domain-joined servers (on-prem) | No | Use standard WMI/SMB |
| Azure/AWS/GCP VMs | Yes | Agent polls over HTTPS |
| DMZ servers | Yes | Avoids opening WMI ports |
| Workgroup machines | Yes | No domain trust required |
| Machines behind NAT | Yes | Outbound-only connections |

---

## Architecture

### Agent Components

The agent consists of:

1. **SysmonConfigPusher.Agent.exe** - Main service executable
2. **agent.json** - Configuration file
3. **Windows Service** - Runs as `SysmonConfigPusherAgent`

### Communication Flow

1. **Registration**: Agent registers with server using a shared token
2. **Heartbeat**: Agent polls server every 30 seconds (configurable)
3. **Commands**: Server queues commands, agent retrieves via heartbeat
4. **Results**: Agent reports command results back to server

### Security Model

- **Registration Token**: Shared secret for initial registration
- **Auth Token**: Unique per-agent token issued after registration
- **TLS**: All communication over HTTPS
- **Minimal Privileges**: Agent only executes Sysmon-related commands

---

## Installation

### Prerequisites

- Windows Server 2016+ or Windows 10+
- .NET 8 Runtime (included in MSI)
- Network access to SysmonConfigPusher server (HTTPS)
- Administrator privileges for installation

### Option 1: MSI Installer (Recommended)

1. Download `SysmonConfigPusherAgent-x.x.x.msi` from [Releases](https://github.com/Antonlovesdnb/SysmonConfigPusher2/releases)

2. Install with parameters:
   ```powershell
   msiexec /i SysmonConfigPusherAgent-2.1.0.msi `
       SERVER_URL="https://your-server:5001" `
       REGISTRATION_TOKEN="your-token-here" `
       /qn
   ```

3. Verify installation:
   ```powershell
   Get-Service SysmonConfigPusherAgent
   ```

### Option 2: Manual Installation

1. Extract agent files to a directory (e.g., `C:\Program Files\SysmonConfigPusherAgent`)

2. Create `agent.json`:
   ```json
   {
     "ServerUrl": "https://your-server:5001",
     "RegistrationToken": "your-registration-token",
     "PollIntervalSeconds": 30,
     "Tags": ["production", "web-server"]
   }
   ```

3. Install as Windows service:
   ```powershell
   sc.exe create SysmonConfigPusherAgent `
       binPath= "C:\Program Files\SysmonConfigPusherAgent\SysmonConfigPusher.Agent.exe" `
       start= auto `
       DisplayName= "SysmonConfigPusher Agent"

   sc.exe start SysmonConfigPusherAgent
   ```

### Installation Parameters

| Parameter | Description | Required |
|-----------|-------------|----------|
| `SERVER_URL` | URL of SysmonConfigPusher server | Yes |
| `REGISTRATION_TOKEN` | Token configured in server settings | Yes |
| `TAGS` | Comma-separated tags for grouping | No |

---

## Configuration

### Server-Side Configuration

1. Navigate to **Settings** > **Agent Settings**

2. Configure:
   - **Registration Token**: Secure token agents must provide
   - **Poll Interval**: How often agents check for commands (10-300 seconds)

3. Save settings

### Agent Configuration File

Location: `C:\Program Files\SysmonConfigPusherAgent\agent.json`

```json
{
  "ServerUrl": "https://sysmonpusher.corp.local:5001",
  "RegistrationToken": "your-secure-token-here",
  "PollIntervalSeconds": 30,
  "Tags": ["production", "web-tier"],
  "ValidateCertificate": true
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `ServerUrl` | Server endpoint URL | (required) |
| `RegistrationToken` | Shared registration secret | (required) |
| `PollIntervalSeconds` | Heartbeat interval | 30 |
| `Tags` | Agent tags for grouping | [] |
| `ValidateCertificate` | Validate server TLS cert | true |

### Tags

Tags help organize agent-managed computers:

```json
{
  "Tags": ["production", "web-server", "us-east-1"]
}
```

Tags appear in the Inventory view with cyan badges.

---

## Agent Management

### Viewing Agent-Managed Computers

1. Navigate to **Inventory**
2. Agent-managed computers show:
   - **Source**: "Agent" badge (purple)
   - **Tags**: Cyan badges with configured tags
   - **Last Heartbeat**: When agent last checked in

### Deploying to Agents

Deployments to agent-managed computers work the same as WMI/SMB deployments:

1. Select agent-managed computers in Inventory
2. Click **Deploy to Selected**
3. Choose operation (Install, Update Config, etc.)
4. Commands are queued and delivered via next heartbeat

### Agent Commands

| Command | Description |
|---------|-------------|
| `GetStatus` | Retrieve Sysmon status and config hash |
| `PushConfig` | Deploy new Sysmon configuration |
| `InstallSysmon` | Install Sysmon with optional config |
| `UninstallSysmon` | Remove Sysmon |
| `QueryEvents` | Retrieve Sysmon event logs |

### Noise Analysis

Noise analysis works with agent-managed computers:

1. Navigate to **Noise Analysis**
2. Select an agent-managed computer
3. Analysis queries events via the agent

---

## Troubleshooting

### Agent Won't Start

**Check service status:**
```powershell
Get-Service SysmonConfigPusherAgent
Get-EventLog -LogName Application -Source "SysmonConfigPusher*" -Newest 10
```

**Common issues:**
- Invalid `agent.json` syntax
- Server URL unreachable
- Invalid registration token

### Agent Shows "Offline"

**On the agent machine:**
```powershell
# Check service is running
Get-Service SysmonConfigPusherAgent

# Test connectivity to server
Test-NetConnection -ComputerName your-server -Port 5001

# Check agent logs
Get-Content "C:\ProgramData\SysmonConfigPusherAgent\logs\*.log" -Tail 50
```

### Registration Failed

**Symptoms:** Agent starts but doesn't appear in inventory

**Check:**
1. Registration token matches server configuration
2. Server URL is correct (include port)
3. TLS certificate is valid (or set `ValidateCertificate: false` for testing)

### Commands Not Executing

**Check command queue:**
1. Navigate to computer details in Inventory
2. View pending commands

**On agent machine:**
```powershell
# Check if Sysmon is accessible
& "C:\Windows\Sysmon64.exe" -c
```

### Certificate Errors

For self-signed certificates during testing:

```json
{
  "ValidateCertificate": false
}
```

**Warning:** Only use this for testing. Use valid certificates in production.

---

## Security Considerations

The agent is designed with defense-in-depth principles to prevent arbitrary command execution, even if the server or network is compromised.

### Protection Against Arbitrary Command Execution

The agent implements multiple layers of security to ensure it can **only** perform Sysmon-related operations:

#### 1. Command Type Whitelisting

The agent only accepts a fixed set of command types. There is no mechanism for the server to execute arbitrary commands:

| Allowed Command | Purpose |
|-----------------|---------|
| `GetStatus` | Query Sysmon installation status |
| `InstallSysmon` | Install Sysmon with provided binary/config |
| `UpdateConfig` | Apply a new Sysmon configuration |
| `UninstallSysmon` | Remove Sysmon |
| `QueryEvents` | Read Sysmon event logs |
| `RestartSysmon` | Restart the Sysmon service |

Any command type not in this list is rejected with an error.

#### 2. Executable Whitelisting

The agent can only execute two specific executables:
- `Sysmon.exe`
- `Sysmon64.exe`

Attempts to execute any other binary are blocked and logged as security violations.

#### 3. Argument Whitelisting

Only these Sysmon command-line arguments are permitted:
- `-accepteula` (accept EULA)
- `-i` (install)
- `-c` (configure)
- `-u` (uninstall)
- `-h` (help)

Arguments like `-n` (network capture) or any other flags are rejected.

#### 4. Binary Validation

Before executing a Sysmon binary (even one received from the server), the agent validates:
- The file's digital signature metadata
- Company name contains "Microsoft", "Sysinternals", or "Mark Russinovich"
- Product name or description contains "Sysmon"

Binaries failing validation are deleted and the command is rejected.

#### 5. Configuration Integrity

When the server sends a configuration:
- A SHA-256 hash is computed and compared against the expected hash
- Mismatches are rejected as potential tampering
- Configurations are written to a controlled directory before being applied

### Authentication Model

#### Two-Stage Authentication

1. **Registration Token**: A shared secret configured on both server and agent. Required for initial registration only.

2. **Auth Token**: After successful registration, the server issues a unique per-agent authentication token. All subsequent requests require this token.

If an agent's auth token becomes invalid (e.g., server restart, token rotation), the agent automatically re-registers using the registration token.

#### Token Best Practices

- Use a strong, random registration token (32+ characters)
- Rotate tokens periodically
- Use different tokens for different environments (prod/dev/staging)

Generate a secure token:
```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

### Network Security

#### Agent-Initiated Connections Only

The agent uses a **pull model** - it initiates all connections to the server:
- No inbound firewall rules required on agent machines
- No listening ports on the agent
- Works through NAT and restrictive firewalls
- Server cannot "push" to agents; it queues commands for pickup

#### TLS Encryption

- All communication over HTTPS (TLS 1.2+)
- Certificate validation enabled by default
- Self-signed certificates can be allowed for testing (not recommended for production)

#### Network Recommendations

- Use valid TLS certificates in production
- Consider IP allowlisting on the server to restrict which IPs can register agents
- Monitor for agents with unexpected hostnames or IPs

### What the Agent Cannot Do

Even if an attacker compromises the server, they **cannot** use the agent to:

- Execute arbitrary commands or scripts
- Run PowerShell, cmd.exe, or other interpreters
- Download and execute arbitrary binaries
- Access files outside the Sysmon directory
- Modify system settings beyond Sysmon
- Exfiltrate data (agents only report Sysmon status and events)
- Pivot to other systems (no lateral movement capability)

### Service Account

The agent runs as SYSTEM by default. This is required to:
- Install/manage Sysmon (requires admin rights)
- Read event logs
- Write to Program Files

The agent's code is designed to minimize the attack surface despite running with elevated privileges.

---

## Uninstalling

### Via MSI

```powershell
msiexec /x SysmonConfigPusherAgent-2.1.0.msi /qn
```

### Manual Uninstall

```powershell
# Stop and remove service
Stop-Service SysmonConfigPusherAgent
sc.exe delete SysmonConfigPusherAgent

# Remove files
Remove-Item "C:\Program Files\SysmonConfigPusherAgent" -Recurse -Force
Remove-Item "C:\ProgramData\SysmonConfigPusherAgent" -Recurse -Force
```

---

## FAQ

**Q: Can I use both agents and WMI/SMB for different machines?**

Yes! The server supports both methods simultaneously. Use WMI/SMB for domain-joined on-prem machines and agents for cloud/DMZ machines.

**Q: What happens if the agent loses connectivity?**

The agent continues attempting to connect. Commands are queued server-side until the agent checks in.

**Q: Can agents work through a proxy?**

Currently, direct HTTPS connectivity is required. Proxy support may be added in a future release.

**Q: How do I update the agent?**

Deploy the new MSI or replace the executable and restart the service. The agent will re-register with the same auth token.
