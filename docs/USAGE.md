# SysmonConfigPusher Usage Guide

This guide covers how to use SysmonConfigPusher to manage Sysmon across your Windows endpoints.

## Table of Contents

- [Getting Started](#getting-started)
- [Dashboard](#dashboard)
- [Inventory Management](#inventory-management)
- [Configuration Management](#configuration-management)
- [Deployments](#deployments)
- [Event Viewer](#event-viewer)
- [Noise Analysis](#noise-analysis)
- [Settings](#settings)

---

## Getting Started

### First Login

1. Open your browser and navigate to `https://your-server:5001`
2. You'll be automatically authenticated via Windows Integrated Auth
3. Your role (Admin, Operator, or Viewer) is determined by AD group membership

### Role Permissions

| Role | View | Deploy | Manage Configs | Settings |
|------|------|--------|----------------|----------|
| Viewer | ✅ | ❌ | ❌ | ❌ |
| Operator | ✅ | ✅ | ✅ | ❌ |
| Admin | ✅ | ✅ | ✅ | ✅ |

---

## Dashboard

The dashboard provides an overview of your Sysmon deployment status:

- **Computers** - Total count, with/without Sysmon installed
- **Configurations** - Number of uploaded configs
- **Deployments** - Recent deployment activity and success rate
- **Sysmon Versions** - Distribution of Sysmon versions across endpoints

---

## Inventory Management

### Populating from Active Directory

1. Navigate to **Inventory**
2. Click **Populate from AD**
3. The system queries AD for all computer objects
4. New computers are added, existing ones are updated

### Scanning Inventory

After populating from AD, scan to detect Sysmon status:

1. Click **Scan Inventory**
2. The system connects to each host via WMI
3. Progress bar shows scan status
4. Results show:
   - **Green badge** - Sysmon version (installed)
   - **Gray "Not installed"** - Host online, no Sysmon
   - **Orange "Offline"** - Host unreachable
   - **"Not scanned"** - Never scanned

### Selecting Computers

- Click a row to select/deselect
- Use the checkbox header to select all
- Selected computers can be targeted for deployment

---

## Configuration Management

### Uploading a Configuration

1. Navigate to **Configurations**
2. Click **Upload Config**
3. Choose a Sysmon XML configuration file
4. The system validates the XML and extracts the SCPTAG (if present)

### Configuration Tags (SCPTAG)

Sysmon configs can include a tag comment for identification:

```xml
<!--SCPTAG:Production-v1.5-->
<Sysmon schemaversion="4.90">
  ...
</Sysmon>
```

This tag is displayed in the inventory after deployment.

### Importing from URL

1. Click **Import from URL**
2. Enter a URL to a raw XML config (e.g., GitHub raw URL)
3. Popular configs:
   - [SwiftOnSecurity/sysmon-config](https://github.com/SwiftOnSecurity/sysmon-config)
   - [olafhartong/sysmon-modular](https://github.com/olafhartong/sysmon-modular)

### Comparing Configurations

1. Select two configurations using the checkboxes
2. Click **Compare Selected**
3. View a side-by-side diff of the configs

---

## Deployments

### Deployment Wizard

1. Navigate to **Deploy** or click **Deploy to Selected** from Inventory
2. Select target computers (if not pre-selected)
3. Choose operation:

| Operation | Description | Requires Config |
|-----------|-------------|-----------------|
| **Install Sysmon** | Install Sysmon binary with optional config | Optional |
| **Update Config** | Push new config to existing Sysmon | Required |
| **Uninstall Sysmon** | Remove Sysmon from targets | No |
| **Test Connectivity** | Verify WMI access to hosts | No |

4. Select configuration (if applicable)
5. Review and confirm
6. Monitor real-time progress

### Deployment Process

For **Install Sysmon**:
1. Creates remote directory (`C:\SysmonFiles` by default)
2. Copies Sysmon binary via SMB
3. Copies config file via SMB
4. Executes `Sysmon64.exe -accepteula -i config.xml` via WMI

For **Update Config**:
1. Copies new config via SMB
2. Executes `Sysmon64.exe -c config.xml` via WMI

### Viewing Deployment Results

1. Navigate to **Deployments**
2. Click on a deployment job to see details
3. View per-host results with success/failure messages

### Scheduled Deployments

1. In the Deploy wizard, toggle **Schedule for later**
2. Select date and time
3. The deployment will execute automatically at the scheduled time

---

## Event Viewer

Query Sysmon event logs from remote hosts without RDP.

### Querying Events

1. Navigate to **Events**
2. Select target computers
3. Set filters:
   - **Event Type** - Process Create, Network Connection, DNS Query, etc.
   - **Time Range** - Last hour, 24 hours, 7 days, or custom
   - **Process Name** - Filter by process
   - **Image Path** - Filter by executable path
   - **Destination IP** - Filter network events
   - **DNS Query** - Filter DNS events
4. Click **Query Events**

### Event Types

| Event ID | Type | Description |
|----------|------|-------------|
| 1 | Process Create | New process started |
| 3 | Network Connection | Outbound network connection |
| 7 | Image Loaded | DLL loaded into process |
| 11 | File Create | File created |
| 22 | DNS Query | DNS lookup performed |

[Full list of Sysmon event types](https://docs.microsoft.com/en-us/sysinternals/downloads/sysmon)

### Viewing Event Details

Click on any event row to expand and view:
- Full command line
- Parent process information
- Network details (for connection events)
- Raw XML

---

## Noise Analysis

Identify high-volume, low-value events to tune your Sysmon configuration.

### Running Analysis

1. Navigate to **Noise Analysis**
2. Select a target computer
3. Choose time range (1-24 hours)
4. Click **Analyze**

### Understanding Results

Events are scored and categorized:

| Level | Indicator | Meaning |
|-------|-----------|---------|
| **Normal** | Green | Within expected thresholds |
| **Noisy** | Yellow | Exceeds threshold by 2x |
| **Very Noisy** | Red | Exceeds threshold by 5x |

### Thresholds

Default thresholds vary by event type:

| Event Type | Events/Hour Threshold |
|------------|----------------------|
| Process Create | 200 |
| Network Connection | 500 |
| Image Loaded | 2000 |
| File Create | 1000 |
| DNS Query | 300 |

### Generating Exclusions

1. Review noisy patterns
2. Select patterns to exclude
3. Click **Generate Exclusions**
4. Copy the generated XML rules
5. Add to your Sysmon configuration

Example generated exclusion:
```xml
<ProcessCreate onmatch="exclude">
  <Image condition="is">C:\Windows\System32\svchost.exe</Image>
</ProcessCreate>
```

---

## Settings

Admin users can configure application settings.

### Authorization Groups

Map AD groups to application roles:

```
Admin Group: SysmonPusher-Admins
Operator Group: SysmonPusher-Operators
Viewer Group: SysmonPusher-Viewers
```

### Sysmon Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Sysmon Binary URL** | Download location for Sysmon | live.sysinternals.com |
| **Default Parallelism** | Concurrent deployment operations | 50 |
| **Remote Directory** | Target directory on endpoints | C:\SysmonFiles |

### Restarting the Service

Some settings require a service restart:
1. Make changes in Settings
2. If prompted, click **Restart Service**
3. Wait for service to restart (page will reload)

---

## Tips & Best Practices

### Deployment Strategy

1. **Test first** - Use "Test Connectivity" before large deployments
2. **Start small** - Deploy to a pilot group first
3. **Use scheduling** - Schedule deployments for maintenance windows
4. **Monitor logs** - Check deployment results for failures

### Configuration Management

1. **Use SCPTAGs** - Tag configs for easy identification
2. **Version control** - Keep configs in git
3. **Test configs** - Validate on test hosts before production
4. **Compare before deploying** - Use diff feature to review changes

### Noise Reduction

1. **Baseline first** - Run noise analysis before tuning
2. **Exclude carefully** - Don't over-exclude security-relevant events
3. **Document exclusions** - Comment why each exclusion exists
4. **Re-analyze** - Run analysis after config changes

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Host shows "Offline" | Check firewall, WMI service, network connectivity |
| Deployment fails | Check service account permissions on target |
| No events returned | Verify Sysmon is running, check time range |
| Auth prompt in browser | Add site to Intranet zone (see Installation Guide) |
