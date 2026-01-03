# SysmonConfigPusher Docker Image
# This image runs in AgentOnly mode - WMI/SMB/AD features are not available
# Use API key authentication for access

# Build stage for the .NET application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for restore (service and its dependencies only)
COPY src/SysmonConfigPusher.Core/*.csproj ./src/SysmonConfigPusher.Core/
COPY src/SysmonConfigPusher.Data/*.csproj ./src/SysmonConfigPusher.Data/
COPY src/SysmonConfigPusher.Infrastructure/*.csproj ./src/SysmonConfigPusher.Infrastructure/
COPY src/SysmonConfigPusher.Service/*.csproj ./src/SysmonConfigPusher.Service/
COPY src/SysmonConfigPusher.Shared/*.csproj ./src/SysmonConfigPusher.Shared/

# Restore the service project and its dependencies
RUN dotnet restore src/SysmonConfigPusher.Service/SysmonConfigPusher.Service.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish src/SysmonConfigPusher.Service/SysmonConfigPusher.Service.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Build frontend
FROM node:20-alpine AS frontend-build
WORKDIR /src

# Copy package files
COPY src/SysmonConfigPusher.Web/package*.json ./src/SysmonConfigPusher.Web/

# Install dependencies
WORKDIR /src/src/SysmonConfigPusher.Web
RUN npm ci

# Copy source and build
COPY src/SysmonConfigPusher.Web/ ./
# Create output directory for build
RUN mkdir -p ../SysmonConfigPusher.Service/wwwroot
RUN npm run build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install OpenSSL for certificate generation
RUN apt-get update && apt-get install -y --no-install-recommends openssl && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r sysmonpusher && useradd -r -g sysmonpusher sysmonpusher

# Copy published application
COPY --from=build /app/publish .

# Copy frontend build output (overwrites any existing wwwroot from publish)
COPY --from=frontend-build /src/src/SysmonConfigPusher.Service/wwwroot ./wwwroot

# Create data, logs, and certs directories
RUN mkdir -p /data/logs /app/certs && chown -R sysmonpusher:sysmonpusher /data /app/certs

# Generate self-signed certificate
RUN openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout /app/certs/server.key \
    -out /app/certs/server.crt \
    -subj "/C=US/ST=State/L=City/O=SysmonConfigPusher/CN=localhost" && \
    openssl pkcs12 -export -out /app/certs/server.pfx \
    -inkey /app/certs/server.key -in /app/certs/server.crt \
    -passout pass:changeit && \
    chown -R sysmonpusher:sysmonpusher /app/certs

# Environment variables
ENV ASPNETCORE_URLS=https://+:5001;http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/server.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=changeit
ENV ServerMode=AgentOnly
ENV Authentication__Mode=ApiKey
ENV ConnectionStrings__DefaultConnection="Data Source=/data/sysmon.db"

# User-friendly environment variables (set these when running the container):
# - API_KEY_ADMIN: API key for Admin role access
# - API_KEY_OPERATOR: API key for Operator role access (optional)
# - API_KEY_VIEWER: API key for Viewer role access (optional)
# - AGENT_TOKEN: Registration token for agents to connect

# Expose both HTTP and HTTPS ports
EXPOSE 5000 5001

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/health/live || exit 1

# Switch to non-root user
USER sysmonpusher

# Start the application
ENTRYPOINT ["dotnet", "SysmonConfigPusher.Service.dll"]
