# Docker Deployment Guide

SysmonConfigPusher can be deployed as a Docker container for environments where Windows Server deployment is not feasible or for cloud-based agent management.

## Overview

When running in Docker, SysmonConfigPusher operates in **AgentOnly mode**, which means:

- **Available Features:**
  - Agent-based deployments (agents connect to the server)
  - Configuration management
  - Deployment history and tracking
  - All UI functionality

- **Unavailable Features (require Windows Server):**
  - WMI-based remote execution
  - SMB file transfers
  - Active Directory computer enumeration

## Quick Start

### Using Docker Compose (Recommended)

1. **Set environment variables** for secure API keys:

```bash
export API_KEY_ADMIN="your-secure-admin-key"
export API_KEY_OPERATOR="your-secure-operator-key"
export API_KEY_VIEWER="your-secure-viewer-key"
export AGENT_TOKEN="your-secure-agent-registration-token"
```

2. **Start the container:**

```bash
docker-compose up -d
```

3. **Access the web UI** at http://localhost:5000

4. **Sign in** using your API key

### Using Docker Run

```bash
docker run -d \
  --name sysmonpusher \
  -p 5000:5000 \
  -v sysmonpusher-data:/data \
  -e API_KEY_ADMIN="your-admin-key" \
  -e AGENT_TOKEN="your-agent-token" \
  ghcr.io/antonlovesdnb/sysmonconfigpusher2:latest
```

For multiple roles:
```bash
docker run -d \
  --name sysmonpusher \
  -p 5000:5000 \
  -v sysmonpusher-data:/data \
  -e API_KEY_ADMIN="admin-secret-key" \
  -e API_KEY_OPERATOR="operator-secret-key" \
  -e API_KEY_VIEWER="viewer-secret-key" \
  -e AGENT_TOKEN="agent-registration-token" \
  ghcr.io/antonlovesdnb/sysmonconfigpusher2:latest
```

## Configuration

### Environment Variables

**User-Friendly Variables (Recommended):**

| Variable | Description | Required |
|----------|-------------|----------|
| `API_KEY_ADMIN` | API key for Admin role | Yes (at least one key) |
| `API_KEY_OPERATOR` | API key for Operator role | No |
| `API_KEY_VIEWER` | API key for Viewer role | No |
| `AGENT_TOKEN` | Registration token for agents | Yes (if using agents) |

**Advanced Variables:**

| Variable | Description | Default |
|----------|-------------|---------|
| `ServerMode` | Server operation mode | `AgentOnly` |
| `Authentication__Mode` | Authentication mode | `ApiKey` |
| `Agent__PollIntervalSeconds` | Agent polling interval | `30` |

### API Key Roles

- **Admin**: Full access to all features including settings
- **Operator**: Can deploy configurations and manage agents
- **Viewer**: Read-only access to view configurations and status

### Using a Configuration File

Instead of environment variables, you can mount a configuration file:

```bash
docker run -d \
  --name sysmonpusher \
  -p 5000:5000 \
  -v sysmonpusher-data:/data \
  -v ./appsettings.Docker.json:/app/appsettings.Production.json:ro \
  sysmonpusher:latest
```

See `appsettings.Docker.json` in the repository root for a sample configuration.

## Production Deployment

### HTTPS with Reverse Proxy

For production, use a reverse proxy like nginx or Traefik to handle TLS termination:

```yaml
# docker-compose.prod.yml
version: '3.8'

services:
  sysmonpusher:
    build: .
    expose:
      - "5000"
    environment:
      - ServerMode=AgentOnly
      - Authentication__Mode=ApiKey
      # ... other env vars
    networks:
      - internal

  nginx:
    image: nginx:alpine
    ports:
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./certs:/etc/nginx/certs:ro
    networks:
      - internal
    depends_on:
      - sysmonpusher

networks:
  internal:
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sysmonpusher
spec:
  replicas: 1
  selector:
    matchLabels:
      app: sysmonpusher
  template:
    metadata:
      labels:
        app: sysmonpusher
    spec:
      containers:
      - name: sysmonpusher
        image: sysmonpusher:latest
        ports:
        - containerPort: 5000
        env:
        - name: ServerMode
          value: "AgentOnly"
        - name: Authentication__Mode
          value: "ApiKey"
        envFrom:
        - secretRef:
            name: sysmonpusher-secrets
        volumeMounts:
        - name: data
          mountPath: /data
        livenessProbe:
          httpGet:
            path: /health/live
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health
            port: 5000
          initialDelaySeconds: 5
          periodSeconds: 10
      volumes:
      - name: data
        persistentVolumeClaim:
          claimName: sysmonpusher-pvc
---
apiVersion: v1
kind: Secret
metadata:
  name: sysmonpusher-secrets
type: Opaque
stringData:
  Authentication__ApiKeys__0__Key: "your-admin-key"
  Authentication__ApiKeys__0__Name: "Admin"
  Authentication__ApiKeys__0__Role: "Admin"
  Agent__RegistrationToken: "your-agent-token"
```

## Agent Configuration

When connecting agents to a Docker-deployed server:

1. Configure the agent's `appsettings.json`:

```json
{
  "ServerUrl": "http://your-docker-host:5000",
  "RegistrationToken": "your-agent-token",
  "PollIntervalSeconds": 30
}
```

2. Or use environment variables when running the agent

## Health Checks

The container exposes two health endpoints:

- `/health` - Full health check including database connectivity
- `/health/live` - Simple liveness probe

## Data Persistence

The SQLite database is stored at `/data/sysmon.db`. To persist data:

```bash
# Named volume (recommended)
-v sysmonpusher-data:/data

# Or bind mount
-v /path/on/host:/data
```

## Backup and Recovery

**⚠️ Important:** If you delete the Docker volume, you lose all data including configurations, deployment history, agent registrations, and audit logs. Always maintain backups!

### What's Stored in the Database

| Data | Impact if Lost |
|------|----------------|
| **Configurations** | Must re-upload all Sysmon configs |
| **Deployment History** | Lose audit trail of all deployments |
| **Agent Registrations** | Agents must re-register |
| **Computer Inventory** | Lose cached host information |
| **Scheduled Deployments** | Scheduled jobs are lost |
| **Noise Analysis Results** | Must re-run analysis |
| **Audit Log** | Lose compliance trail |

### Manual Backup

**Option 1: Copy the database file**

```bash
# Stop the container to ensure consistency
docker compose stop

# Copy the database from the volume
docker run --rm -v sysmonpusher-data:/data -v $(pwd)/backups:/backup alpine \
  cp /data/sysmon.db /backup/sysmon-$(date +%Y%m%d-%H%M%S).db

# Restart the container
docker compose start
```

**Option 2: Use bind mount for easy access**

```yaml
# docker-compose.yml
services:
  sysmonpusher:
    volumes:
      - ./data:/data  # Data accessible at ./data/sysmon.db
```

Then backup with:
```bash
cp ./data/sysmon.db ./backups/sysmon-$(date +%Y%m%d-%H%M%S).db
```

### Automated Backup Script

Create `backup.sh`:

```bash
#!/bin/bash
BACKUP_DIR="/path/to/backups"
CONTAINER_NAME="sysmonpusher"
KEEP_DAYS=30

# Create backup directory
mkdir -p "$BACKUP_DIR"

# Copy database (SQLite supports hot backup for reads)
docker exec $CONTAINER_NAME sqlite3 /data/sysmon.db ".backup '/tmp/sysmon-backup.db'"
docker cp $CONTAINER_NAME:/tmp/sysmon-backup.db "$BACKUP_DIR/sysmon-$(date +%Y%m%d-%H%M%S).db"

# Clean old backups
find "$BACKUP_DIR" -name "sysmon-*.db" -mtime +$KEEP_DAYS -delete

echo "Backup completed: $BACKUP_DIR"
```

Schedule with cron:
```bash
# Daily backup at 2 AM
0 2 * * * /path/to/backup.sh
```

### Exporting Configurations

To backup just your Sysmon configurations (portable, version-controllable):

**Via API:**

```bash
# Get all configs (requires API key with Viewer role)
curl -H "X-Api-Key: your-api-key" \
  https://your-server:5000/api/configs \
  -o configs-backup.json

# Get a specific config's XML content
curl -H "X-Api-Key: your-api-key" \
  https://your-server:5000/api/configs/1 \
  | jq -r '.content' > my-config.xml
```

**Via Docker exec:**

```bash
# Export all configs as JSON
docker exec sysmonpusher sqlite3 -json /data/sysmon.db \
  "SELECT id, filename, tag, content, hash, uploadedAt FROM Configs WHERE isActive = 1" \
  > configs-export.json

# Export a single config's XML
docker exec sysmonpusher sqlite3 /data/sysmon.db \
  "SELECT content FROM Configs WHERE id = 1" > config-1.xml
```

### Restoring from Backup

**Full database restore:**

```bash
# Stop the container
docker compose stop

# Replace the database
docker run --rm -v sysmonpusher-data:/data -v $(pwd)/backups:/backup alpine \
  cp /backup/sysmon-20240115-020000.db /data/sysmon.db

# Start the container
docker compose start
```

**Importing configs from XML files:**

Use the web UI's "Upload Config" or "Import from URL" features, or:

```bash
# Upload via API
curl -X POST -H "X-Api-Key: your-admin-key" \
  -H "Content-Type: multipart/form-data" \
  -F "file=@my-config.xml" \
  https://your-server:5000/api/configs
```

### Migrating to a New Server

1. **Backup the database:**
   ```bash
   docker exec sysmonpusher sqlite3 /data/sysmon.db ".backup '/tmp/migration.db'"
   docker cp sysmonpusher:/tmp/migration.db ./migration.db
   ```

2. **Copy to new server:**
   ```bash
   scp migration.db newserver:/path/to/data/
   ```

3. **On new server, start with the database:**
   ```bash
   # Copy database to new volume
   docker run --rm -v sysmonpusher-data:/data -v $(pwd):/backup alpine \
     cp /backup/migration.db /data/sysmon.db

   # Start the container
   docker compose up -d
   ```

4. **Update agent configurations** to point to the new server URL

### Disaster Recovery Checklist

If you lose your Docker volume:

1. ✅ Restore database from backup (see above)
2. ✅ If no backup: re-upload Sysmon configurations from version control
3. ✅ Agents will automatically re-register when they poll the server
4. ✅ Re-run inventory scan to repopulate computer list
5. ⚠️ Deployment history will be lost (cannot be recovered without backup)
6. ⚠️ Scheduled deployments must be recreated

### Best Practices

1. **Use bind mounts** instead of named volumes for easier backup access
2. **Version control your configs** - store Sysmon XML files in git
3. **Automate backups** - schedule daily database backups
4. **Test restores** - periodically verify backups work
5. **Document your setup** - keep docker-compose.yml and env vars in version control

## Building the Image

```bash
# Build the image
docker build -t sysmonpusher:latest .

# Build with a specific tag
docker build -t sysmonpusher:v2.2.0 .
```

## Troubleshooting

### Cannot connect to the server

1. Check the container is running: `docker ps`
2. View logs: `docker logs sysmonpusher`
3. Verify port mapping: `docker port sysmonpusher`

### Authentication fails

1. Verify API key is correct
2. Check environment variables: `docker exec sysmonpusher env | grep Auth`
3. Ensure the API key role has required permissions

### Database issues

1. Check data volume is mounted: `docker inspect sysmonpusher`
2. Verify permissions on the data directory
3. View database location: The database is at `/data/sysmon.db`

### Agent registration fails

1. Verify the registration token matches
2. Check network connectivity from agent to server
3. Review server logs for registration attempts
