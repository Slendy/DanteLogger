# DanteLogger

A simple command line application to log audio loss events in Dante digital audio networks.

## How to run
Head to the [releases](https://github.com/Slendy/DanteLogger/releases/latest) page to download the latest binary for your platform. Once downloaded the program can be ran from the terminal like so:
### MacOS
```bash
# remove quarantine from downloaded file
xattr -d com.apple.quarantine DanteLogger-macOS
./DanteLogger-macOS
```
### Windows
x64
```
DanteLogger-win_x64.exe
```
Arm64
```
DanteLogger-win_arm64.exe
```

### Linux
x64
```bash
./DanteLogger-linux_x64
```
Arm64
```bash
./DanteLogger-linux_arm64
```

DanteLogger will automatically listen on all interfaces and will log any audio loss events to the console and log file.

Specific device information can be queried by using the `substatus <ip address>` command liks so:
```bash
./DanteLogger substatus 169.254.0.14
```

## Building
This app requires .NET 10 SDK and can be ran using the following command when in the DanteLogger project directory
```bash
dotnet run
```
The final trimmed app can be built per platform like so:
```bash
dotnet publish -r (runtime identifier) -p:PublishTrimmed=true
```
For a list of runtime identifiers see the [RID catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog#known-rids)

The final binary will be in the `/bin/Release/net10.0/(runtime identifier)/publish` folder.

