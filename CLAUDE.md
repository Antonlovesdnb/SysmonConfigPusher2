# SysmonConfigPusher v2 - Web Modernization Project Specification

## Project Overview

Modernize the existing [SysmonConfigPusher](https://github.com/LaresLLC/SysmonConfigPusher) tool from a WPF desktop application to a web-based service. The original tool pushes Sysmon configurations to Windows endpoints in an Active Directory environment without requiring agents.

### Original Tool Summary
- Written in C# / WPF (.NET 4.0)
- Uses WMI (Win32_Process.Create) for remote command execution
- Uses LDAP for AD computer enumeration
- Embedded HTTP server for config file distribution
- Operations: Create directories, push Sysmon binary, install Sysmon, push configs, update configs, uninstall Sysmon

---

## Requirements

### Functional Requirements
1. **Agentless Operation**: Push Sysmon configs, install/uninstall Sysmon, and perform system operations on remote hosts WITHOUT an agent
2. **Web Application**: Modern, easy-to-use GUI accessible via web browser
3. **Remote Event Log Viewing**: View Sysmon event logs from remote hosts in the AD environment
4. **Noise Analysis**: Identify "noisy" Sysmon events to help tune configurations (NEW FEATURE)

### Non-Functional Requirements
- Scale from lab environments (5-10 hosts) to large deployments (2000+ hosts)
- Single-server deployment model
- Windows Integrated Authentication (Kerberos/NTLM)

---

## Technical Decisions (Confirmed)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Remote Execution | WMI (Win32_Process.Create) | Matches original tool; works reliably |
| File Transfer | SMB (admin shares) | Simpler than embedded HTTP server; direct push |
| Event Log Queries | Remote EventLog API | Native .NET; no PowerShell overhead |
| Authentication | Windows Integrated Auth | Enterprise standard; no separate credentials |
| Database | SQLite | Simple; single-server; no external dependencies |
| Frontend | React + TypeScript + Tailwind | Modern; maintainable; good ecosystem |
| Backend | ASP.NET Core 8 | Native Windows integration; runs as Windows Service |
| Real-time Updates | SignalR | Built-in; WebSocket-based progress streaming |
| Sysmon Binary | Cached from live.sysinternals.com | Offline capability; version tracking |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Browser (Domain-Joined Workstation)             │
│                          Windows Auth (Kerberos)                    │
└─────────────────────────────────────┬───────────────────────────────┘
                                      │ HTTPS (443)
                                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│            Windows Server (Domain-Joined, Single Instance)          │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │         ASP.NET Core 8 (Kestrel + Windows Auth)               │  │
│  │  • REST API Controllers                                       │  │
│  │  • SignalR Hub (real-time deployment progress)                │  │
│  │  • Background Workers (deployment queue, binary cache)        │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              │                                      │
│  ┌───────────────────────────┴───────────────────────────────────┐  │
│  │                     Core Services                             │  │
│  │  • ActiveDirectoryService (LDAP queries)                      │  │
│  │  • RemoteExecutionService (WMI Win32_Process)                 │  │
│  │  • FileTransferService (SMB to admin shares)                  │  │
│  │  • EventLogService (remote Sysmon log queries)                │  │
│  │  • NoiseAnalysisService (event aggregation + scoring)         │  │
│  │  • SysmonBinaryCacheService (download + cache Sysmon.exe)     │  │
│  │  • ConfigParserService (SCPTAG parsing)                       │  │
│  │  • ThresholdService (role-based noise thresholds)             │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              │                                      │
│  ┌───────────────────────────┴───────────────────────────────────┐  │
│  │                     SQLite Database                           │  │
│  │  • Computers (inventory cache)                                │  │
│  │  • ComputerGroups (saved selections)                          │  │
│  │  • Configs (uploaded configs with versions)                   │  │
│  │  • DeploymentJobs + DeploymentResults                         │  │
│  │  • NoiseAnalysisRuns + NoiseResults                           │  │
│  │  • AuditLog                                                   │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                               │
          WMI (135 + dynamic) + SMB (445) + EventLog (RPC)
                               │
            ┌──────────────────┼──────────────────┐
            ▼                  ▼                  ▼
       Endpoints          Endpoints          Endpoints
      (No Agent)         (No Agent)         (No Agent)
```

---

## Remote Execution Strategy

### Command Execution (Deploy, Install, Update, Uninstall)
- **Method**: WMI Win32_Process.Create
- **Pattern**: Fire-and-forget command execution
- **Verification**: Query remote EventLog for Event ID 16 (config change) or check file existence

### File Transfer (Push Sysmon binary, Push config)
- **Method**: SMB via admin shares (\\\\hostname\\C$\\SysmonFiles\\)
- **Benefit**: Direct file copy; no embedded HTTP server needed; faster than PowerShell Invoke-WebRequest

### Event Log Queries (Viewer, Noise Analysis)
- **Method**: System.Diagnostics.Eventing.Reader.EventLogSession with remote computer
- **Fallback**: WMI if direct EventLog access fails

### Deployment Flow (New Approach)
```
Management Host                          Target Host
      │                                       │
      │──SMB: Create \\target\C$\SysmonFiles─▶│
      │──SMB: Copy Sysmon64.exe──────────────▶│
      │──SMB: Copy config.xml────────────────▶│
      │──WMI: Sysmon -accepteula -i──────────▶│
      │──WMI: Sysmon -c config.xml───────────▶│
      │──EventLog: Query Event ID 16─────────▶│
      │◀────────Config hash verification──────│
```

---

## Noise Analysis Feature (Key New Capability)

### Purpose
Identify high-volume, low-value Sysmon events to help users tune their configurations and reduce log noise.

### Detection Categories
1. **High Volume**: Event count per process/path exceeds role-based threshold
2. **Repetitive**: Same event signature repeating (low entropy in field values)
3. **Known Benign**: Matches curated patterns (Defender, Windows Update, browsers, etc.)

### Smart Thresholds by Host Role

Host roles detected automatically via AD/WMI queries:

| Event Type | Workstation | Server | Domain Controller |
|------------|-------------|--------|-------------------|
| Process Create/hr | 200 | 500 | 1000 |
| Network Conn/hr | 500 | 2000 | 5000 |
| File Create/hr | 1000 | 5000 | 10000 |
| DNS Query/hr | 300 | 500 | 2000 |
| Image Load/hr | 2000 | 5000 | 10000 |

- Events exceeding threshold × 2 = "Noisy" (yellow)
- Events exceeding threshold × 5 = "Very Noisy" (red)

### Output
- Ranked list of noise sources
- Generated Sysmon exclusion rules (ready-to-paste XML)
- Cross-host comparison (identify outliers vs. common noise)

---

## Project Structure

```
SysmonConfigPusher/
├── src/
│   ├── SysmonConfigPusher.Service/           # Windows Service + Web API
│   │   ├── Controllers/
│   │   │   ├── ComputersController.cs
│   │   │   ├── ConfigsController.cs
│   │   │   ├── DeploymentsController.cs
│   │   │   ├── EventsController.cs
│   │   │   └── AnalysisController.cs
│   │   ├── Hubs/
│   │   │   └── DeploymentHub.cs
│   │   ├── BackgroundServices/
│   │   │   ├── DeploymentWorker.cs
│   │   │   └── BinaryCacheWorker.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── SysmonConfigPusher.Core/              # Business logic
│   │   ├── Services/
│   │   │   ├── ActiveDirectoryService.cs
│   │   │   ├── RemoteExecutionService.cs
│   │   │   ├── FileTransferService.cs
│   │   │   ├── EventLogService.cs
│   │   │   ├── SysmonBinaryCacheService.cs
│   │   │   ├── ConfigParserService.cs
│   │   │   ├── NoiseAnalysisService.cs
│   │   │   └── ThresholdService.cs
│   │   ├── Models/
│   │   └── Interfaces/
│   │
│   ├── SysmonConfigPusher.Infrastructure/    # Platform implementations
│   │   ├── Wmi/
│   │   ├── Smb/
│   │   ├── EventLog/
│   │   └── ActiveDirectory/
│   │
│   ├── SysmonConfigPusher.Data/              # EF Core + SQLite
│   │   ├── SysmonDbContext.cs
│   │   ├── Migrations/
│   │   └── Repositories/
│   │
│   └── SysmonConfigPusher.Web/               # React frontend
│       ├── src/
│       │   ├── components/
│       │   ├── pages/
│       │   ├── hooks/
│       │   └── services/
│       └── package.json
│
├── tests/
└── docs/
```

---

## Database Schema

```sql
-- Computer inventory cache
CREATE TABLE Computers (
    Id INTEGER PRIMARY KEY,
    Hostname TEXT NOT NULL UNIQUE,
    DistinguishedName TEXT,
    OperatingSystem TEXT,
    LastSeen DATETIME,
    SysmonVersion TEXT,
    ConfigHash TEXT,
    LastDeployment DATETIME
);

-- Saved computer groups
CREATE TABLE ComputerGroups (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    CreatedBy TEXT,
    CreatedAt DATETIME
);

CREATE TABLE ComputerGroupMembers (
    GroupId INTEGER REFERENCES ComputerGroups(Id),
    ComputerId INTEGER REFERENCES Computers(Id),
    PRIMARY KEY (GroupId, ComputerId)
);

-- Config management
CREATE TABLE Configs (
    Id INTEGER PRIMARY KEY,
    Filename TEXT NOT NULL,
    Tag TEXT,
    Content TEXT NOT NULL,
    Hash TEXT NOT NULL,
    UploadedBy TEXT,
    UploadedAt DATETIME,
    IsActive BOOLEAN DEFAULT 1
);

-- Deployment jobs
CREATE TABLE DeploymentJobs (
    Id INTEGER PRIMARY KEY,
    Operation TEXT NOT NULL,
    ConfigId INTEGER REFERENCES Configs(Id),
    StartedBy TEXT,
    StartedAt DATETIME,
    CompletedAt DATETIME,
    Status TEXT
);

CREATE TABLE DeploymentResults (
    Id INTEGER PRIMARY KEY,
    JobId INTEGER REFERENCES DeploymentJobs(Id),
    ComputerId INTEGER REFERENCES Computers(Id),
    Success BOOLEAN,
    Message TEXT,
    CompletedAt DATETIME
);

-- Noise analysis
CREATE TABLE NoiseAnalysisRuns (
    Id INTEGER PRIMARY KEY,
    ComputerId INTEGER REFERENCES Computers(Id),
    TimeRangeHours INTEGER,
    TotalEvents INTEGER,
    AnalyzedAt DATETIME
);

CREATE TABLE NoiseResults (
    Id INTEGER PRIMARY KEY,
    RunId INTEGER REFERENCES NoiseAnalysisRuns(Id),
    EventId INTEGER,
    GroupingKey TEXT,
    EventCount INTEGER,
    NoiseScore REAL,
    SuggestedExclusion TEXT
);

-- Audit log
CREATE TABLE AuditLog (
    Id INTEGER PRIMARY KEY,
    Timestamp DATETIME,
    User TEXT,
    Action TEXT,
    Details TEXT
);
```

---

## API Endpoints

```
# Computers
GET    /api/computers                    # List with filters
GET    /api/computers/{id}               # Single computer
POST   /api/computers/refresh            # Refresh from AD
POST   /api/computers/test-connectivity  # Test WinRM/WMI access
GET    /api/computers/groups             # List saved groups
POST   /api/computers/groups             # Create group

# Configs
GET    /api/configs                      # List all
GET    /api/configs/{id}                 # Single with content
POST   /api/configs                      # Upload new
DELETE /api/configs/{id}                 # Delete
GET    /api/configs/{id}/diff/{otherId}  # Compare two

# Deployments
POST   /api/deployments                  # Start job
GET    /api/deployments                  # List jobs
GET    /api/deployments/{id}             # Job details + results
DELETE /api/deployments/{id}             # Cancel

# Event Viewer
POST   /api/events/query                 # Query remote events

# Noise Analysis
POST   /api/analysis/noise               # Run analysis
GET    /api/analysis/noise/{runId}       # Get results
POST   /api/analysis/compare             # Cross-host comparison
GET    /api/analysis/exclusions/{runId}  # Get suggested XML

# SignalR Hub
/hubs/deployment                         # Real-time progress
```

---

## Development Phases

### Phase 1: Foundation (1-2 weeks)
- Solution scaffolding with project structure above
- Windows Service hosting with Windows Auth
- SQLite + EF Core setup with migrations
- Health check endpoints
- AD browser service (LDAP computer enumeration)

### Phase 2: Core Operations (2-3 weeks)
- WMI execution service
- SMB file transfer service
- Adaptive parallelism (scale-aware execution)
- All deployment operations
- SignalR progress streaming
- Config management (upload, parse SCPTAG, store)

### Phase 3: Basic Web UI (2 weeks)
- React project with Vite
- Computer browser page
- Config manager page
- Deployment wizard
- Real-time progress display

### Phase 4: Event Viewer (1-2 weeks)
- Remote event log query service
- Event grid with filtering
- Export capability

### Phase 5: Noise Analysis (2-3 weeks)
- Aggregation engine
- Noise scoring with role-based thresholds
- Exclusion rule generator
- Cross-host comparison UI

### Phase 6: Polish (1-2 weeks)
- Dashboard
- Audit logging
- Error handling
- Documentation
- Installer

---

## Getting Started (for Claude Code)

Start with Phase 1:

1. Create the .NET solution with the project structure above
2. Set up the ASP.NET Core Web API project as a Windows Service
3. Configure Windows Authentication
4. Set up SQLite with EF Core and create initial migrations
5. Implement the ActiveDirectoryService for computer enumeration
6. Create basic health check endpoint
7. Scaffold the React frontend with Vite

The service should:
- Run as a Windows Service (Microsoft.Extensions.Hosting.WindowsServices)
- Listen on HTTPS (port 5001 for dev, configurable for prod)
- Use Windows Authentication (Negotiate)
- Store data in SQLite at %ProgramData%\SysmonConfigPusher\sysmon.db
- Serve the React SPA from wwwroot

## General

- Use relative instead of absolute paths

---

## Documentation TODO

The following items need to be documented before release:

- [ ] **TLS Certificate Configuration**: Document how to configure production TLS certificates in appsettings.json:
  - PFX file method (Path + Password)
  - Windows Certificate Store method (Subject + Store + Location)
  - Certificate renewal procedures
  - Troubleshooting common certificate issues