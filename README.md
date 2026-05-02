# DanteLogger

A simple command line application to log audio loss events in Dante digital audio networks.

## How to run
Head to the [releases](https://github.com/Slendy/DanteLogger/releases/latest) page to download the latest binary for your platform. Once downloaded the program can be ran from the terminal like so:
### MacOS
```bash
# remove quarantine from downloaded file
xattr -d com.apple.quarantine
./DanteLogger-macos
```
### Windows
```
DanteLogger.exe
```

### Linux
```bash
./DanteLogger
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