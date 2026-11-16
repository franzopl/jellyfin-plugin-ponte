#!/usr/bin/env bash
# Runs the .NET SDK in Docker for machines without a local SDK: ./.dotnet-docker.sh build ...
exec docker run --rm -u "$(id -u):$(id -g)" \
  -e HOME=/home/dev -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  -v "$HOME/.cache/dotnet-docker-home:/home/dev" \
  -v "$(cd "$(dirname "$0")" && pwd):/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"
