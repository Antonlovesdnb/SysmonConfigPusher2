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

# Create non-root user for security
RUN groupadd -r sysmonpusher && useradd -r -g sysmonpusher sysmonpusher

# Copy published application
COPY --from=build /app/publish .

# Copy frontend build output (overwrites any existing wwwroot from publish)
COPY --from=frontend-build /src/src/SysmonConfigPusher.Service/wwwroot ./wwwroot

# Create data and logs directories
RUN mkdir -p /data/logs && chown -R sysmonpusher:sysmonpusher /data

# Environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ServerMode=AgentOnly
ENV Authentication__Mode=ApiKey
ENV ConnectionStrings__DefaultConnection="Data Source=/data/sysmon.db"

# Expose HTTP port (use reverse proxy for HTTPS in production)
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/health/live || exit 1

# Switch to non-root user
USER sysmonpusher

# Start the application
ENTRYPOINT ["dotnet", "SysmonConfigPusher.Service.dll"]
