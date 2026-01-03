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
  -e Authentication__Mode=ApiKey \
  -e "Authentication__ApiKeys__0__Key=your-admin-key" \
  -e "Authentication__ApiKeys__0__Name=Admin" \
  -e "Authentication__ApiKeys__0__Role=Admin" \
  -e "Agent__RegistrationToken=your-agent-token" \
  sysmonpusher:latest
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ServerMode` | Server operation mode | `AgentOnly` |
| `Authentication__Mode` | Authentication mode (`ApiKey`, `Windows`) | `ApiKey` |
| `Authentication__ApiKeyHeader` | Header name for API key | `X-Api-Key` |
| `Authentication__ApiKeys__N__Key` | API key value (N = 0, 1, 2...) | - |
| `Authentication__ApiKeys__N__Name` | Display name for API key | - |
| `Authentication__ApiKeys__N__Role` | Role: `Admin`, `Operator`, or `Viewer` | - |
| `Agent__RegistrationToken` | Token for agent registration | - |
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
