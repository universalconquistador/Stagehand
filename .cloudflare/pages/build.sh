#!/bin/sh
curl -sSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version
#./dotnet/dotnet publish -c Release -o output
./dotnet/dotnet tool update docfx

./dotnet/dotnet tool run docfx Docs/docfx.json