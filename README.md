# README #

This README would normally document whatever steps are necessary to get your application up and running.

### What is this repository for? ###

Command Center provide the ability to transmit video from a camera, plugged-in to the PC, to UgCS video server. We have a special app for this - Video Transmitter. The app is included in the Command Center installer. [Learn more](https://sphengineering.atlassian.net/wiki/spaces/CC/pages/1788903439/Web+cam+video+transmission)

### How do I build on CI? ###

* Prerequisites: [.Net Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/visual-studio-sdks?utm_source=getdotnetsdk&utm_medium=referral), msbuild 15.
* To build the solution execute `msbuild /t:restore;build /p:Configuration=Release;Version=<version>;FileVersion=<version>;AssemblyVersion=<version>` from `/src/VideoTransmitter/` directory. Where `<version>` shoudl be replaced with the actual build version.