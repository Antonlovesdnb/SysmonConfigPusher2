# Deployment Modes: Full vs Agent-Only

SysmonConfigPusher supports two deployment modes to accommodate different infrastructure environments. This guide explains the differences, trade-offs, and when to use each mode.

## Quick Comparison

| Feature | Full Mode | Agent-Only Mode |
|---------|-----------|-----------------|
| **Deployment platform** | Windows Server (domain-joined) | Any (Linux, Docker, Windows) |
| **Computer discovery** | Active Directory LDAP | Manual or agent self-registration |
| **Remote execution** | WMI (Win32_Process.Create) | Agent polls and executes |
| **File transfer** | SMB (admin shares) | Agent pulls configs |
| **Event log queries** | Direct EventLog API | Agent queries locally |
| **Authentication** | Windows Integrated (Kerberos) | API keys |
| **Network requirements** | Outbound to endpoints | Inbound from agents |
| **Firewall rules** | Many ports required | Only HTTPS (443/5001) |
| **Best for** | On-premises domain environments | Cloud, DMZ, hybrid |

---

## Full Mode

Full mode provides complete functionality using native Windows protocols for agentless management.

### How It Works

```
┌────────────────────────────────────────────────────────────────┐
│           SysmonConfigPusher Server (Windows)                  │
│                   Domain-Joined                                │
└───────────────────────────┬────────────────────────────────────┘
                            │
        WMI (135 + dynamic) │ SMB (445) │ EventLog (RPC)
                            │
      ┌─────────────────────┼─────────────────────┐
      ▼                     ▼                     ▼
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│  Endpoint   │       │  Endpoint   │       │  Endpoint   │
│ (No Agent)  │       │ (No Agent)  │       │ (No Agent)  │
│   Domain    │       │   Domain    │       │   Domain    │
│   Joined    │       │   Joined    │       │   Joined    │
└─────────────┘       └─────────────┘       └─────────────┘
```

### Features Available

| Feature | Full Mode |
|---------|-----------|
| Populate from Active Directory | ✅ |
| Inventory scan via WMI | ✅ |
| Deploy Sysmon via WMI/SMB | ✅ |
| Update configs via WMI/SMB | ✅ |
| Uninstall Sysmon via WMI | ✅ |
| Query event logs remotely | ✅ |
| Noise analysis | ✅ |
| Scheduled deployments | ✅ |
| Windows Integrated Auth | ✅ |
| Agent-based management | ✅ (hybrid) |

### Requirements

- **Server**: Windows Server 2016+ (domain-joined)
- **Service Account**: Domain account with local admin on endpoints
- **Network**:
  - TCP 135 (WMI/RPC mapper)
  - TCP 445 (SMB)
  - TCP 49152-65535 (Dynamic RPC)
- **Endpoints**: Must be reachable from server

### When to Use Full Mode

✅ **Use Full Mode when:**
- All endpoints are domain-joined Windows machines
- Server can directly reach endpoints on WMI/SMB ports
- You want agentless management (no software on endpoints)
- You're in a traditional on-premises Active Directory environment
- You need instant deployments (no polling delay)

❌ **Avoid Full Mode when:**
- Endpoints are in the cloud (Azure, AWS, GCP)
- Endpoints are behind NAT or firewalls blocking WMI/SMB
- You have non-domain-joined (workgroup) machines
- You cannot open the required firewall ports

### Configuration

Full mode is the default on Windows Server:

```json
{
  "ServerMode": "Full"
}
```

Or explicitly in `appsettings.json`:

```json
{
  "ServerMode": "Full",
  "Authentication": {
    "Mode": "Windows"
  },
  "Authorization": {
    "AdminGroup": "SysmonPusher-Admins",
    "OperatorGroup": "SysmonPusher-Operators",
    "ViewerGroup": "SysmonPusher-Viewers"
  }
}
```

---

## Agent-Only Mode

Agent-only mode uses lightweight agents on endpoints that poll the server for commands.

### How It Works

```
┌────────────────────────────────────────────────────────────────┐
│           SysmonConfigPusher Server                            │
│         (Linux, Docker, or Windows)                            │
│              HTTPS only (5001)                                 │
└───────────────────────────▲────────────────────────────────────┘
                            │
                     HTTPS (outbound)
                     Agent-initiated
                            │
      ┌─────────────────────┼─────────────────────┐
      │                     │                     │
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│    Agent    │       │    Agent    │       │    Agent    │
│  (Cloud VM) │       │ (DMZ Server)│       │ (Workgroup) │
│   Polls     │       │   Polls     │       │   Polls     │
│   server    │       │   server    │       │   server    │
└─────────────┘       └─────────────┘       └─────────────┘
```

### Features Available

| Feature | Agent-Only Mode |
|---------|-----------------|
| Populate from Active Directory | ❌ (agents self-register) |
| Inventory scan | ✅ (agent-initiated) |
| Deploy Sysmon | ✅ (via agent) |
| Update configs | ✅ (via agent) |
| Uninstall Sysmon | ✅ (via agent) |
| Query event logs | ✅ (via agent) |
| Noise analysis | ✅ (via agent) |
| Scheduled deployments | ✅ |
| Windows Integrated Auth | ❌ (API keys) |
| WMI/SMB operations | ❌ |

### Requirements

- **Server**: Any platform (Linux, Docker, Windows)
- **Agents**: Windows endpoints with agent installed
- **Network**:
  - Agents must reach server on HTTPS (port 5001 or 443)
  - No inbound connections required to agents
- **Authentication**: API keys for web UI access

### When to Use Agent-Only Mode

✅ **Use Agent-Only Mode when:**
- Endpoints are in the cloud (Azure VMs, AWS EC2, GCP)
- Endpoints are behind NAT or restrictive firewalls
- You have workgroup (non-domain) machines
- Server is containerized (Docker, Kubernetes)
- Server is Linux-based
- You cannot open WMI/SMB ports between server and endpoints
- Endpoints are in a DMZ

❌ **Avoid Agent-Only Mode when:**
- You want truly agentless management
- All machines are on-prem with full network access
- You don't want to install/maintain software on endpoints

### Configuration

Agent-only mode is automatic on non-Windows systems or can be forced:

```json
{
  "ServerMode": "AgentOnly",
  "Authentication": {
    "Mode": "ApiKey",
    "ApiKeyHeader": "X-Api-Key",
    "ApiKeys": [
      { "Key": "admin-key-here", "Name": "Admin", "Role": "Admin" },
      { "Key": "operator-key-here", "Name": "Operator", "Role": "Operator" }
    ]
  },
  "Agent": {
    "RegistrationToken": "your-secure-token"
  }
}
```

---

## Hybrid Mode

You can use **both modes simultaneously** on a Windows Server, supporting both agentless WMI/SMB management for on-prem machines and agent-based management for cloud/DMZ machines.

### How It Works

```
┌────────────────────────────────────────────────────────────────┐
│           SysmonConfigPusher Server (Windows)                  │
│                   Full Mode                                    │
└───────────────┬───────────────────────────▲────────────────────┘
                │                           │
    WMI/SMB (direct)              HTTPS (agent-initiated)
                │                           │
      ┌─────────┴─────────┐       ┌─────────┴─────────┐
      ▼                   ▼       ▼                   ▼
┌───────────┐       ┌───────────┐ ┌───────────┐ ┌───────────┐
│ On-Prem   │       │ On-Prem   │ │ Cloud VM  │ │ DMZ Host  │
│ Endpoint  │       │ Endpoint  │ │  (Agent)  │ │  (Agent)  │
│ (No Agent)│       │ (No Agent)│ └───────────┘ └───────────┘
└───────────┘       └───────────┘
```

### Configuration for Hybrid

Just run Full mode - agent support is included:

```json
{
  "ServerMode": "Full",
  "Agent": {
    "RegistrationToken": "your-secure-token"
  }
}
```

In the Inventory, you'll see:
- **WMI/SMB badge**: Machines discovered via AD, managed via WMI/SMB
- **Agent badge**: Machines with agents, managed via agent communication

---

## Trade-offs Comparison

### Deployment Speed

| Mode | Speed | Why |
|------|-------|-----|
| Full | Instant | Direct WMI execution |
| Agent-Only | Up to poll interval | Agent must poll server |

Agent-only has a delay equal to the poll interval (default 30 seconds). For urgent deployments, consider lowering the interval temporarily.

### Network Complexity

| Mode | Complexity | Ports Required |
|------|------------|----------------|
| Full | Higher | 135, 445, 49152-65535 |
| Agent-Only | Lower | Single HTTPS port |

Full mode requires multiple firewall rules and may not work across VLANs with strict ACLs.

### Operational Overhead

| Mode | Overhead | Reason |
|------|----------|--------|
| Full | Lower (no agents) | Nothing to install on endpoints |
| Agent-Only | Higher | Must install/update agents |

Agent-only requires agent deployment and lifecycle management, but provides more flexibility.

### Security Model

| Aspect | Full Mode | Agent-Only Mode |
|--------|-----------|-----------------|
| Attack surface (server) | Requires AD access | API keys only |
| Attack surface (endpoints) | WMI/SMB exposure | Agent with limited capabilities |
| Credential exposure | Service account | Registration token |
| Network exposure | Multiple protocols | Single HTTPS |

Agent-only may be preferred in high-security environments due to:
- Reduced network exposure (HTTPS only)
- Agent runs locally (no network credential exposure)
- Agent is sandboxed (can only run Sysmon commands)

### Scalability

| Mode | Scalability | Bottleneck |
|------|-------------|------------|
| Full | Limited by parallelism | WMI connections |
| Agent-Only | Better | Agents do local work |

For very large deployments (2000+ hosts), agent-only mode scales better because:
- Agents execute locally (no remote connection overhead)
- Server only queues commands (low resource usage)
- No WMI connection limits

---

## Decision Matrix

Use this matrix to choose the right mode for your environment:

| Scenario | Recommended Mode |
|----------|------------------|
| On-prem, domain-joined, full network access | Full |
| Azure/AWS/GCP VMs | Agent-Only |
| Hybrid (on-prem + cloud) | Hybrid (Full + Agents) |
| DMZ servers | Agent-Only |
| Workgroup machines | Agent-Only |
| Docker/Kubernetes deployment | Agent-Only |
| Linux server | Agent-Only |
| < 100 hosts, on-prem | Full (simpler) |
| > 1000 hosts | Agent-Only (scales better) |
| Restrictive firewall policy | Agent-Only |
| No agents allowed on endpoints | Full |

---

## Migration Paths

### Full Mode → Agent-Only Mode

1. Deploy Docker/Linux server with agent-only mode
2. Install agents on endpoints (can coexist with WMI management)
3. Verify agents are registered and functional
4. Decommission Windows server or switch to agent-only mode

### Agent-Only → Full Mode

1. Deploy Windows Server, domain-join it
2. Configure service account with endpoint access
3. Populate inventory from AD
4. Test WMI/SMB connectivity
5. Agents can remain for DMZ/cloud machines (hybrid mode)

---

## FAQ

**Q: Can I mix WMI/SMB and agent-managed machines?**

Yes! Run Full mode on Windows Server and install agents on machines that can't be reached via WMI/SMB. Both management methods work simultaneously.

**Q: What happens to agents if I switch to Full mode?**

Agents continue to work. Full mode includes agent support. You can manage some machines via WMI/SMB and others via agents.

**Q: Is there a performance difference for deployments?**

Full mode is faster (instant execution) but has parallelism limits. Agent-only mode has a polling delay but can scale to more hosts since agents execute locally.

**Q: Which mode is more secure?**

Both are secure when configured correctly. Agent-only mode has a smaller network attack surface. Full mode doesn't require additional software on endpoints.

**Q: Can I start with agent-only and add AD later?**

Yes. Deploy agent-only first, then add a Windows Server for hybrid mode to get AD integration while keeping agent management for cloud machines.
