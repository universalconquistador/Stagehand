#!/bin/sh
curl -sSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version
#./dotnet/dotnet publish -c Release -o output
echo "Creating dotnet tool manifest..."
./dotnet/dotnet new tool-manifest
./dotnet/dotnet tool update docfx


curl --output latest.zip -L "https://goatcorp.github.io/dalamud-distrib/latest.zip"

unzip latest.zip -d /opt/buildhome/repo/dalamud-distrib
export DALAMUD_HOME=/opt/buildhome/repo/dalamud-distrib

./dotnet/dotnet tool run docfx Docs/docfx.json