# Security

Please report suspected vulnerabilities privately through the public repository's Security Advisories reporting feature when enabled. If no private reporting channel is available, open an issue requesting a private contact without exploit details, personal logs or credentials. This preview has no security response-time guarantee.

The main app runs as the user. Optional Acer temperature access requests a separate elevated reader only after a Settings action. It uses a fixed telemetry getter, authenticated local named pipes, bounded sessions and watchdogs. It cannot install drivers or change fans/clocks. Exact model, BIOS, runtime and host checks intentionally fail closed.

Portable builds carry an app-private .NET runtime. Updating a globally installed runtime does not update that copy: maintainers must review current advisories and rebuild/distribute patched portable packages. No automatic network updater is included.

CPU-driver/service experiments are outside the public app and package. No telemetry or network listener is implemented. An absence of external packages and earlier reviews are not proof that software has no vulnerabilities. Recheck the SDK/runtime and operating-system advisories before distributing binaries.
