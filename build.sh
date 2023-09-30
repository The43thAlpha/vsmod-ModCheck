#!/bin/bash

dotnet clean || exit
rm -r bin/ obj/
dotnet build -c Release || exit
cd bin/ || exit
unzip ModCheck-UNVERSIONED.zip -d ModCheck/ || exit
rm ModCheck-UNVERSIONED.zip || exit
cd ModCheck || exit
version="$(jq ".version" -r < modinfo.json)"
zip ../ModCheck-"$version".zip ModCheck.* modinfo.json || exit
rm /usr/share/vintagestory-server/Mods/ModCheck-*
cp ../ModCheck-"$version".zip /usr/share/vintagestory-server/Mods/
cd ../.. || exit
