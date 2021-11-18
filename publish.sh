#!/bin/bash

dotnet publish -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o publish/linux-x64 DevTools\DevTools.csproj
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/win-x64 DevTools\DevTools.csproj