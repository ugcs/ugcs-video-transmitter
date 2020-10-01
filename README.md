# UgCS Video Transmitter #

## What is Video Transmitter? ##

It is a desktop app that captures video from the device connected to the PC (USB camera, for example), displays the video preview on the screen and provides the ability to transmit this video stream to a UgCS Video Server.

## Supported platforms ##

Windows x64

## How to build ##

Prerequisites: [.Net Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/visual-studio-sdks?utm_source=getdotnetsdk&utm_medium=referral), msbuild 15.
1. Execute `msbuild /t:restore;build /p:Configuration=Release`.
1. Download the FFmpeg shared binaries [64-bit](https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full-shared.zip).
1. Extract the contents of the zip file you just downloaded and go to the bin folder that got extracted. You should see 3 exe files and multiple dll files. Select and copy all .dll files.
1. Now paste all files from the prior step onto a build output folder: `\src\VideoTransmitter\VideoTransmitter\bin\x64\Release\net4.7.2`.