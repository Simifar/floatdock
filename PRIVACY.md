# Privacy

FloatDock does not collect, store on a server, or send any personal data. It has no accounts, no analytics and no telemetry.

## What stays on your PC

- **Settings and pins** in `%LocalAppData%\FloatDock\settings.json`: your choices, and the apps, folders and files you pinned (their paths).
- **Caches**: the list of installed apps, how often you start apps from the app menu (a count per app, used for the "Frequent" row), copies of the shortcuts you pinned.
- **Logs** in `%LocalAppData%\FloatDock\logs`: event names and error types, no window titles, file names or typed text.
- **Crash dumps** in `%LocalAppData%\FloatDock\crashes` after an unhandled error (the last five). A dump holds thread stacks and module lists and can contain fragments of data from memory. It is never sent anywhere; delete it before sharing the folder.

To remove all of it, uninstall FloatDock and delete the `%LocalAppData%\FloatDock` folder. If you installed it from the Microsoft Store, the same data sits in the package's own folder and is removed with the app.

## What the dock reads

To show running apps it reads the titles, icons, process names and positions of your open windows, and the list of installed apps. This stays in memory in the app and is never written to the logs or sent anywhere.

## Network use

None, unless you press **Check for updates** in Settings or turn on **Download updates at startup**. Then the app asks the update source in Settings (by default the project's releases on GitHub, `https://github.com/Simifar/floatdock`) for the list of releases and, if you choose to, downloads the update. That request reveals your IP address and the app version to GitHub, like any download; GitHub's own [privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) applies. Clearing the update source field turns the feature off completely.

The copy from the Microsoft Store does not use this updater; the Store updates it, and Microsoft's own terms and privacy statement apply to that.

## Changes

Any change to this page is part of a commit in the project's history.
