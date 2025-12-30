# SysmonConfigPusher v2

![CI](https://github.com/Antonlovesdnb/SysmonConfigPusher2/actions/workflows/ci.yml/badge.svg)

A web-based tool for pushing Sysmon configurations to Windows endpoints in an Active Directory environment without requiring agents.

## Features

- **Agentless Operation** - Push Sysmon configs, install/uninstall Sysmon via WMI and SMB
- **Web Interface** - Modern React UI accessible via browser
- **Remote Event Log Viewing** - View Sysmon logs from remote hosts
- **Noise Analysis** - Identify high-volume events to tune configurations
- **Scheduled Deployments** - Schedule deployments for future execution
- **Windows Integrated Auth** - Uses Kerberos/NTLM authentication

## Tech Stack

- **Backend**: ASP.NET Core 8, Entity Framework Core, SQLite
- **Frontend**: React, TypeScript, Tailwind CSS, Vite
- **Real-time**: SignalR for deployment progress

## Quick Start

### Prerequisites

- Windows Server (domain-joined)
- .NET 8 SDK
- Node.js 20+

### Development

```bash
# Backend
cd src/SysmonConfigPusher.Service
dotnet run --urls "https://localhost:5001"

# Frontend (separate terminal)
cd src/SysmonConfigPusher.Web
npm install
npm run dev
```

Access the app at `https://localhost:5001`

### Running Tests

```bash
dotnet test
```

## Architecture

```
Browser (Windows Auth)
    │
    ▼ HTTPS
┌─────────────────────────────────┐
│  ASP.NET Core Windows Service   │
│  • REST API + SignalR Hub       │
│  • Background Workers           │
│  • SQLite Database              │
└─────────────────────────────────┘
    │
    ▼ WMI + SMB + EventLog
┌─────────────────────────────────┐
│  Target Endpoints (No Agent)    │
└─────────────────────────────────┘
```

## License

See [LICENSE](LICENSE) for details.
