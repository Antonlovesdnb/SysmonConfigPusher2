#!/bin/sh
set -e

# Ensure data directories exist with proper permissions
mkdir -p /data/logs

# Start the application
exec dotnet SysmonConfigPusher.Service.dll "$@"
