# SysmonConfigPusher Development Guide

This guide is for developers who want to build SysmonConfigPusher from source.

## Prerequisites

- Windows 10/11 or Windows Server 2016+
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/) (for frontend development)
- Git
- Visual Studio 2022 or VS Code (optional)

## Clone and Build

```powershell
# Clone the repository
git clone https://github.com/Antonlovesdnb/SysmonConfigPusher2.git
cd SysmonConfigPusher2

# Build the frontend
cd src/SysmonConfigPusher.Web
npm install
npm run build
cd ../..

# Build the backend
dotnet build
```

## Running in Development Mode

```powershell
# Start the backend (from repo root)
cd src/SysmonConfigPusher.Service
dotnet run

# Or with hot reload
dotnet watch run
```

The API will be available at https://localhost:5001

For frontend development with hot reload:
```powershell
cd src/SysmonConfigPusher.Web
npm run dev
```

The Vite dev server runs at http://localhost:5173 and proxies API requests.

## Development Configuration

Development mode uses `appsettings.json` with these defaults:
- **DisableAuth**: Set to `true` to bypass Windows authentication (dev only)
- **Swagger UI**: Available at https://localhost:5001/swagger

## Project Structure

```
SysmonConfigPusher2/
├── src/
│   ├── SysmonConfigPusher.Core/          # Business logic, interfaces
│   ├── SysmonConfigPusher.Data/          # EF Core, SQLite
│   ├── SysmonConfigPusher.Infrastructure/ # WMI, SMB, AD implementations
│   ├── SysmonConfigPusher.Service/       # ASP.NET Core Web API
│   └── SysmonConfigPusher.Web/           # React frontend
├── tests/                                 # Unit and integration tests
├── scripts/                               # Operational scripts
├── installer/                             # WiX MSI installer
└── docs/                                  # Documentation
```

## Running Tests

```powershell
dotnet test
```

## Building for Release

### Self-Contained Publish

Creates a standalone package with .NET runtime included:

```powershell
# Build frontend first
cd src/SysmonConfigPusher.Web
npm run build
cd ../..

# Publish self-contained
dotnet publish src/SysmonConfigPusher.Service -c Release -r win-x64 --self-contained -o publish
```

### Building the MSI Installer

Requires [WiX Toolset v4](https://wixtoolset.org/):

```powershell
# Install WiX CLI tool
dotnet tool install --global wix

# Build the installer
cd installer
wix build -o SysmonConfigPusher.msi SysmonConfigPusher.wxs
```

## Database

SQLite database is stored at:
- Development: `%ProgramData%\SysmonConfigPusher\sysmon.db`

To reset the database:
```powershell
Remove-Item "$env:ProgramData\SysmonConfigPusher\sysmon.db" -Force
```

Database migrations run automatically on startup.

## Debugging Remote Operations

For testing WMI/SMB operations without a full AD environment:
1. Use local computer name as target
2. Ensure Windows Remote Management is enabled
3. Run with elevated permissions

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests
5. Submit a pull request
