# TLS Certificate Configuration Guide

This guide covers all aspects of TLS certificate configuration for SysmonConfigPusher, including the web server, agent connections, and production deployment scenarios.

## Table of Contents

- [Overview](#overview)
- [Server Certificate (Web UI)](#server-certificate-web-ui)
- [Agent Certificate Validation](#agent-certificate-validation)
- [Docker and Reverse Proxy TLS](#docker-and-reverse-proxy-tls)
- [Production Recommendations](#production-recommendations)
- [Troubleshooting](#troubleshooting)

---

## Overview

SysmonConfigPusher uses TLS for two communication channels:

1. **Web UI / API**: HTTPS connection between browsers/API clients and the server
2. **Agent Communication**: HTTPS connection between agents and the server

```
┌──────────────┐                  ┌─────────────────────────────┐
│   Browser    │──── HTTPS ────▶  │  SysmonConfigPusher Server  │
│   (Web UI)   │   (Port 5001)    │                             │
└──────────────┘                  │   Certificate required for   │
                                  │   - Web UI                   │
┌──────────────┐                  │   - API endpoints            │
│    Agent     │──── HTTPS ────▶  │   - Agent communication      │
│  (Endpoint)  │   (Port 5001)    │                             │
└──────────────┘                  └─────────────────────────────┘
```

---

## Server Certificate (Web UI)

The server needs a TLS certificate to serve HTTPS traffic. There are three options:

### Option 1: Auto-Generated Self-Signed Certificate (Default)

On Windows, the service automatically creates a self-signed certificate on first start.

**Characteristics:**
- Subject: `CN=SysmonConfigPusher`
- Validity: 2 years
- Key: RSA 2048-bit with SHA256
- SANs: Machine hostname, localhost
- Location: Windows Certificate Store (`LocalMachine\My`)

**Pros:**
- Zero configuration required
- Works out of the box
- Auto-renews when expiring (< 30 days remaining)

**Cons:**
- Browser warnings (untrusted CA)
- Not suitable for production
- Agents must disable certificate validation or pin the certificate

**To use:** Simply start the service - no configuration needed.

### Option 2: Windows Certificate Store (Recommended for Windows Server)

Import your organization's certificate and reference it by subject name.

**Step 1: Import the certificate**

```powershell
# Import PFX to LocalMachine\My
$password = Read-Host -AsSecureString -Prompt "PFX Password"
Import-PfxCertificate -FilePath "C:\path\to\cert.pfx" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -Password $password
```

**Step 2: Grant private key access**

```powershell
# Get the certificate
$cert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
    Where-Object { $_.Subject -match "sysmonpusher" }

# Grant the service account access (LocalSystem needs it)
$keyPath = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
$keyFullPath = "C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys\$keyPath"
$acl = Get-Acl $keyFullPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "SYSTEM", "Read", "Allow")
$acl.AddAccessRule($rule)
Set-Acl $keyFullPath $acl
```

Or via GUI:
1. Open `certlm.msc`
2. Navigate to Personal > Certificates
3. Right-click your certificate > All Tasks > Manage Private Keys
4. Add read permission for `SYSTEM` (or your service account)

**Step 3: Configure appsettings.json**

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

**Certificate Store Options:**

| Property | Values | Description |
|----------|--------|-------------|
| `Store` | `My`, `Root`, `CA` | Certificate store name |
| `Location` | `LocalMachine`, `CurrentUser` | Store location |
| `Subject` | Certificate subject | Must match exactly (e.g., `CN=hostname`) |
| `Thumbprint` | Certificate thumbprint | Alternative to Subject matching |

**Using Thumbprint (more precise):**

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Thumbprint": "A1B2C3D4E5F6...",
          "Store": "My",
          "Location": "LocalMachine"
        }
      }
    }
  }
}
```

### Option 3: PFX File

Use a PFX/PKCS12 file directly.

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Path": "C:\\ProgramData\\SysmonConfigPusher\\server.pfx",
          "Password": "your-pfx-password"
        }
      }
    }
  }
}
```

**Security considerations:**
- Store the PFX file with restricted permissions
- Consider using environment variables for the password:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Path": "C:\\ProgramData\\SysmonConfigPusher\\server.pfx",
          "Password": "${CERT_PASSWORD}"
        }
      }
    }
  }
}
```

Then set: `$env:CERT_PASSWORD = "your-password"`

---

## Agent Certificate Validation

Agents connect to the server over HTTPS. You can configure how they validate the server's certificate.

### Agent Configuration File

Location: `C:\Program Files\SysmonConfigPusher\Agent\agent.json`

```json
{
  "serverUrl": "https://sysmonpusher.corp.local:5001",
  "registrationToken": "your-token",
  "pollIntervalSeconds": 30,
  "validateServerCertificate": true,
  "certificateThumbprint": null
}
```

### Validation Modes

#### Mode 1: Full Validation (Default, Production)

```json
{
  "validateServerCertificate": true,
  "certificateThumbprint": null
}
```

The agent validates:
- Certificate is signed by a trusted CA
- Certificate is not expired
- Certificate hostname matches the server URL
- Certificate chain is valid

**Requirements:**
- Server must use a certificate from a trusted CA (internal or public)
- The CA's root certificate must be in the agent machine's Trusted Root store

#### Mode 2: Certificate Pinning (High Security)

```json
{
  "validateServerCertificate": true,
  "certificateThumbprint": "A1B2C3D4E5F6789012345678901234567890ABCD"
}
```

The agent only accepts the specific certificate matching this thumbprint.

**Pros:**
- Protects against CA compromise
- Works with self-signed certificates
- Highest security option

**Cons:**
- Must update all agents when certificate rotates
- More operational overhead

**Getting the thumbprint:**

```powershell
# On the server
$cert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
    Where-Object { $_.Subject -match "sysmonpusher" }
$cert.Thumbprint
```

Or from the PFX:
```powershell
$cert = Get-PfxCertificate -FilePath "server.pfx"
$cert.Thumbprint
```

#### Mode 3: Disable Validation (Testing Only)

```json
{
  "validateServerCertificate": false,
  "certificateThumbprint": null
}
```

**⚠️ WARNING:** Only use this for testing! Disabling validation exposes agents to man-in-the-middle attacks.

### MSI Installation with Certificate Settings

```powershell
# With certificate validation disabled (testing)
msiexec /i SysmonConfigPusherAgent.msi `
    SERVER_URL="https://test-server:5001" `
    REGISTRATION_TOKEN="test-token" `
    VALIDATE_CERTIFICATE="false" `
    /qn

# With certificate pinning
msiexec /i SysmonConfigPusherAgent.msi `
    SERVER_URL="https://prod-server:5001" `
    REGISTRATION_TOKEN="prod-token" `
    CERTIFICATE_THUMBPRINT="A1B2C3D4..." `
    /qn
```

---

## Docker and Reverse Proxy TLS

When running SysmonConfigPusher in Docker, TLS is typically handled differently.

### Option 1: TLS Termination at Reverse Proxy (Recommended)

Let nginx, Traefik, or a cloud load balancer handle TLS:

```
┌─────────────┐          ┌──────────────┐          ┌─────────────────┐
│   Agents    │── HTTPS ─▶│  nginx/LB    │── HTTP ─▶│  SysmonPusher   │
│   Browser   │   :443    │  (TLS term)  │   :5000  │   (Container)   │
└─────────────┘          └──────────────┘          └─────────────────┘
```

**docker-compose.yml:**

```yaml
version: '3.8'

services:
  sysmonpusher:
    image: sysmonpusher:latest
    expose:
      - "5000"
    environment:
      - ServerMode=AgentOnly
      - Authentication__Mode=ApiKey
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

**nginx.conf:**

```nginx
events {
    worker_connections 1024;
}

http {
    upstream sysmonpusher {
        server sysmonpusher:5000;
    }

    server {
        listen 443 ssl http2;
        server_name sysmonpusher.yourdomain.com;

        ssl_certificate /etc/nginx/certs/fullchain.pem;
        ssl_certificate_key /etc/nginx/certs/privkey.pem;
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
        ssl_prefer_server_ciphers off;

        location / {
            proxy_pass http://sysmonpusher;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
}
```

### Option 2: TLS Inside Container

If you need TLS directly on the container:

**docker-compose.yml:**

```yaml
services:
  sysmonpusher:
    image: sysmonpusher:latest
    ports:
      - "5001:5001"
    volumes:
      - ./certs:/certs:ro
      - sysmonpusher-data:/data
    environment:
      - Kestrel__Endpoints__Https__Url=https://*:5001
      - Kestrel__Endpoints__Https__Certificate__Path=/certs/server.pfx
      - Kestrel__Endpoints__Https__Certificate__Password=your-password
```

### Option 3: Let's Encrypt with Traefik

```yaml
version: '3.8'

services:
  traefik:
    image: traefik:v2.10
    command:
      - "--api.insecure=true"
      - "--providers.docker=true"
      - "--entrypoints.websecure.address=:443"
      - "--certificatesresolvers.letsencrypt.acme.tlschallenge=true"
      - "--certificatesresolvers.letsencrypt.acme.email=admin@yourdomain.com"
      - "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json"
    ports:
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./letsencrypt:/letsencrypt

  sysmonpusher:
    image: sysmonpusher:latest
    expose:
      - "5000"
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.sysmonpusher.rule=Host(`sysmonpusher.yourdomain.com`)"
      - "traefik.http.routers.sysmonpusher.entrypoints=websecure"
      - "traefik.http.routers.sysmonpusher.tls.certresolver=letsencrypt"
```

---

## Production Recommendations

### For Windows Server Deployments

1. **Use an internal CA certificate**
   - Request a certificate from your organization's PKI
   - Include SANs for all hostnames/IPs clients might use
   - Use RSA 2048-bit or ECC P-256 keys

2. **Configure via Windows Certificate Store**
   - More secure than PFX files on disk
   - Easier key permission management
   - Integrates with enterprise certificate management

3. **Plan for renewal**
   - Set calendar reminders for certificate expiration
   - Consider shorter validity periods (1 year) for easier rotation
   - Test renewal process before certificates expire

### For Docker/Cloud Deployments

1. **Use a reverse proxy for TLS termination**
   - nginx, Traefik, or cloud load balancer
   - Easier certificate management
   - Better performance (TLS offloading)

2. **Consider Let's Encrypt**
   - Free, automated certificate renewal
   - Widely trusted
   - Easy integration with Traefik

3. **For private/internal deployments**
   - Use internal CA certificates
   - Distribute CA root to agent machines
   - Or use certificate pinning

### Agent Certificate Strategy

| Environment | Recommendation |
|-------------|---------------|
| **Lab/Testing** | Disable validation (`validateServerCertificate: false`) |
| **Internal Production** | Full validation with internal CA |
| **High Security** | Certificate pinning with thumbprint |
| **Cloud/Public** | Full validation with public CA (Let's Encrypt) |

### Certificate Checklist

Before going to production:

- [ ] Certificate is from a trusted CA (internal or public)
- [ ] Certificate includes all necessary SANs (hostnames, IPs)
- [ ] Certificate validity is appropriate (1-2 years recommended)
- [ ] Private key is properly secured (permissions, encryption)
- [ ] Agents are configured for appropriate validation mode
- [ ] Renewal process is documented and tested
- [ ] Monitoring/alerting for certificate expiration is in place

---

## Troubleshooting

### Server Won't Start with Certificate

**Error:** `Unable to configure HTTPS endpoint. No server certificate was specified`

**Solutions:**
1. Verify certificate exists in specified store/path
2. Check certificate subject/thumbprint matches configuration
3. Ensure service account has private key access:
   ```powershell
   # Check private key access
   $cert = Get-ChildItem "Cert:\LocalMachine\My\THUMBPRINT"
   $cert.HasPrivateKey  # Should be True
   ```

### Agent Can't Connect (Certificate Errors)

**Error:** `The SSL connection could not be established`

**Check:**
1. Server certificate is valid and not expired
2. Server hostname matches certificate SAN
3. Agent machine trusts the CA (if using full validation)
4. Certificate pinning thumbprint is correct (if using pinning)

**Testing from agent machine:**

```powershell
# Test TLS connection
Test-NetConnection -ComputerName sysmonpusher -Port 5001

# Check certificate details
$webRequest = [Net.WebRequest]::Create("https://sysmonpusher:5001")
try { $webRequest.GetResponse() } catch {}
$cert = $webRequest.ServicePoint.Certificate
$cert | Format-List *
```

### Browser Shows Certificate Warning

**For self-signed certificates:**
1. Click "Advanced" > "Proceed to site" (acceptable for testing)
2. Or import the certificate to Trusted Root:
   ```powershell
   # Export server cert
   $cert = Get-ChildItem "Cert:\LocalMachine\My" | Where Subject -match "SysmonConfigPusher"
   Export-Certificate -Cert $cert -FilePath "server.cer"

   # Import on client (as admin)
   Import-Certificate -FilePath "server.cer" -CertStoreLocation "Cert:\LocalMachine\Root"
   ```

### Certificate Expired

**Symptoms:** Agents stop connecting, browser shows expiration error

**Fix:**
1. Renew/replace the certificate
2. If using auto-generated: restart service (will create new cert)
3. Update agent thumbprints if using certificate pinning

### Mixed HTTP/HTTPS Issues

**Error:** "Mixed content blocked" or redirect loops

**Ensure:**
1. All traffic uses HTTPS
2. Reverse proxy sets `X-Forwarded-Proto: https`
3. Application trusts the forwarded header

---

## Quick Reference

### View Current Certificate (Windows Server)

```powershell
# Find the certificate being used
Get-ChildItem "Cert:\LocalMachine\My" |
    Where Subject -match "SysmonConfigPusher" |
    Select Subject, Thumbprint, NotAfter
```

### Generate Self-Signed for Testing

```powershell
$cert = New-SelfSignedCertificate -DnsName "sysmonpusher","localhost" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -NotAfter (Get-Date).AddYears(2)

# Export for distribution
Export-PfxCertificate -Cert $cert -FilePath "server.pfx" -Password (ConvertTo-SecureString -String "password" -Force -AsPlainText)
```

### Check Certificate Expiration

```powershell
# On server
Get-ChildItem "Cert:\LocalMachine\My" |
    Where Subject -match "SysmonConfigPusher" |
    Select Subject, NotAfter, @{N='DaysRemaining';E={($_.NotAfter - (Get-Date)).Days}}
```
