# Contributing

Use the pinned SDK and run `scripts/build-public.ps1 -Test`. Keep polling off the UI thread, storage bounded and missing readings explicit. Do not introduce network listeners, telemetry, driver installation or privileged actions without an explicit reviewed design.

Do not broaden Acer qualification from a model-name guess. Changes need documented firmware and host compatibility plus a separately authorised live test. Automated tests must use fake readings and must never trigger UAC or firmware access.

Increment the patch component in `Version.props` once for every app update, including small fixes. Settings and package metadata use that same version. A feature/release milestone may instead increment the minor or major component deliberately. Do not bump versions for documentation-only changes or every intermediate build.

Never commit credentials, personal hardware reports, CSV logs, screenshots, binaries or build output. Include relevant build/check evidence in change descriptions, with private details removed.
