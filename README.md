# Win2DS5

Win2DS5 turns a PlayStation DualSense into a mouse and keyboard on Windows.

The installer and later builds are published on [GitHub releases](https://github.com/kmnoiprocs/Win2DS5/releases). Download `Win2DS5-Setup.exe`. It asks where to install, whether you want a desktop shortcut, a Start menu shortcut, and a taskbar pin, and it can download HidHide and ViGEmBus.

An installed copy checks that release page and installs a newer build the next time it starts.

To publish a build from this PC:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-Release.ps1 -Version 1.0.1 -Notes "What changed." -Upload
```

The version has to be newer than the one already published. The script builds the app, the installer, and the update package, then uploads them.
