<p align="center">
  <h1 align="center">SysmonConfigPusher v2</h1>
  <p align="center">
    A web-based tool for managing Sysmon configurations across Windows endpoints in Active Directory environments — no agents required.
  </p>
</p>

<p align="center">
  <a href="https://github.com/Antonlovesdnb/SysmonConfigPusher2/releases/latest"><img src="https://img.shields.io/github/v/release/Antonlovesdnb/SysmonConfigPusher2?style=flat-square&color=blue" alt="Latest Release"></a>
  <a href="https://github.com/Antonlovesdnb/SysmonConfigPusher2/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Antonlovesdnb/SysmonConfigPusher2/ci.yml?style=flat-square&label=build" alt="Build Status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Antonlovesdnb/SysmonConfigPusher2?style=flat-square" alt="License"></a>
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square" alt=".NET 8">
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Windows">
</p>

---

## Features

| Feature | Description |
|---------|-------------|
| **Agentless Deployment** | Push Sysmon binaries and configs via WMI and SMB |
| **Web Interface** | Modern React UI with real-time deployment progress |
| **Event Log Viewer** | Query Sysmon logs from remote hosts |
| **Noise Analysis** | Identify high-volume events to tune configurations |
| **Scheduled Deployments** | Schedule deployments for future execution |
| **Windows Auth** | Integrated Kerberos/NTLM authentication |

## Quick Start

### Option 1: MSI Installer (Recommended)

1. Download the latest [SysmonConfigPusher.msi](https://github.com/Antonlovesdnb/SysmonConfigPusher2/releases/latest)
2. Run the installer as Administrator
3. Configure a domain service account ([see docs](docs/INSTALLATION.md#step-4-configure-service-account))
4. Start the service and access `https://servername:5001`

### Option 2: Build from Source

See the [Development Guide](docs/DEVELOPMENT.md) for building and running locally.

## Documentation

| Guide | Description |
|-------|-------------|
| [Installation Guide](docs/INSTALLATION.md) | Production deployment and configuration |
| [Development Guide](docs/DEVELOPMENT.md) | Building from source, running locally |

## Requirements

- **Server**: Windows Server 2016+ (domain-joined)
- **Service Account**: Domain account with local admin rights on target endpoints
- **Network Ports**:
  - TCP 135 (WMI/RPC)
  - TCP 445 (SMB)
  - TCP 49152-65535 (RPC dynamic)

## Architecture

```
┌──────────────────────────────────────┐
│  Browser (Windows Integrated Auth)   │
└──────────────────┬───────────────────┘
                   │ HTTPS :5001
                   ▼
┌──────────────────────────────────────┐
│     ASP.NET Core Windows Service     │
│  ┌────────────┐  ┌────────────────┐  │
│  │  REST API  │  │  SignalR Hub   │  │
│  └────────────┘  └────────────────┘  │
│  ┌────────────────────────────────┐  │
│  │    SQLite + Background Jobs    │  │
│  └────────────────────────────────┘  │
└──────────────────┬───────────────────┘
                   │ WMI + SMB
                   ▼
┌──────────────────────────────────────┐
│    Target Endpoints (No Agent)       │
└──────────────────────────────────────┘
```

## Tech Stack

- **Backend**: ASP.NET Core 8, Entity Framework Core, SQLite
- **Frontend**: React 18, TypeScript, Tailwind CSS, Vite
- **Real-time**: SignalR WebSockets

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

This is a modernization of the original [SysmonConfigPusher](https://github.com/LaresLLC/SysmonConfigPusher) WPF application.
