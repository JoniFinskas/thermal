# Thermal

A Windows desktop monitor with a smoked-glass interface, red accents, charts, compact mode, a tray menu and hardware specifications.

## Available readings

- CPU load and physical RAM usage through Windows.
- NVIDIA GPU temperature and load through the already installed NVIDIA driver. No NVIDIA DLL or SDK is bundled.
- Optional Acer CPU temperature through an existing firmware interface. Currently qualified only for Predator PHN16-72 with BIOS V1.11 and the reviewed Windows PowerShell/Framework files. Windows servicing can invalidate those exact file pins; the reader then fails closed. Other Acer models are not advertised as supported.

Acer access is off by default and enabled in Settings for the current session only. Windows asks for administrator permission for a separate, read-only PowerShell reader; the app itself stays unelevated. Cancellation leaves ordinary monitoring available. No driver, service, fan controller or tuning tool is installed. The reader stops when Thermal closes, with independent timeout guards. Sessions last at most four hours.

Intel/AMD GPU telemetry is not implemented. CPU load is unavailable above 64 logical processors. Missing sensors are shown honestly rather than estimated. Current qualification is Windows 11 x64; Windows 10, ARM64, other hardware and long sleep/resume or endurance runs are not release-qualified. Glass effects depend on Windows and accessibility settings.

## Build

Install the Microsoft .NET SDK specified in `global.json` separately. From the repository root, use PowerShell 7:

```powershell
./scripts/build-public.ps1 -Test
dotnet run --project src/Thermal.App -c Release -p:ThermalPublicBuild=true --no-restore
```

The build uses no external NuGet or JavaScript packages and downloads no packages, drivers or installers. NuGet feeds are cleared. Public outputs use `bin/public` and `obj/public`. The public repository defaults to the opt-in Acer policy in `Directory.Build.props`.

Portable packaging is a separate reviewed step using `scripts/prepare-app-package.ps1 -PublicCandidate -RuntimeRoot <reviewed-runtime-folder> -OutputRoot <fresh-artifacts-folder>`. It requires an independently reviewed matching runtime and includes its Microsoft notices; it installs nothing. Do not publish build or QA folders as a release.

## Local history and export

Charts retain one or four hours in memory. Optional CSV logging is off by default and writes only to `%LOCALAPPDATA%/Thermal/Logs`, bounded to seven days, ten files and 100 MiB. CSV export also works with logging disabled. CSV includes device names and readings: review files before sharing. No telemetry, cloud upload or network listener is implemented.

Preview binaries are currently unsigned; Windows may display a publisher or reputation warning. Use trusted release checksums or build from source. Do not disable Windows security protections to run the app.

## Licence

Original code is MIT; see `LICENSE`. Adapted Phosphor Icons 2.1.10 are MIT; their full notice and provenance are included. Windows/.NET and installed GPU/firmware drivers have their own terms. Apple frameworks, branding and assets are not included.

This is an early preview, not a certified diagnostic or safety tool. See `CONTRIBUTING.md` and `SECURITY.md`.
