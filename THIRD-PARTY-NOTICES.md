# Third-party notices

FloatDock itself is under the MIT license (see `LICENSE`). The release also contains the following components. The full license texts are in each package (the `license` file inside the NuGet package) and, for the Microsoft components, at the addresses below. This list is checked by hand when a package is added or updated.

| Component | Version | License |
| --- | --- | --- |
| Microsoft Windows App SDK (Base, Foundation, InteractiveExperiences, WinUI, DWrite, Runtime) | 2.0.4, 2.3.12, 2.1.9, 2.3.9, 2.1.0, 2.5.1 | Microsoft Software License Terms, Windows App SDK |
| Microsoft.Windows.SDK.NET (C#/WinRT projection of the Windows SDK) and Windows SDK build tools | 10.0.26100.x | Windows SDK license, <https://aka.ms/WinSDKLicenseURL> |
| Microsoft.Web.WebView2 (shipped with the Windows App SDK files, not used by FloatDock) | 1.0.3719.77 | BSD 3-Clause (Microsoft) |
| Velopack (installer and updater) | 1.2.161 | MIT, Copyright Velopack Ltd |
| System.Drawing.Common, Microsoft.Win32.SystemEvents | 10.0.0 | MIT, Copyright .NET Foundation and Contributors |
| .NET runtime and libraries (self-contained in the release) | 10.0 | MIT, Copyright .NET Foundation and Contributors |

## Microsoft Windows App SDK

The Windows App SDK files that the build copies next to the application are distributed under the Microsoft Software License Terms for the Windows App SDK (section 3, "Distributable Code"). Those terms require anyone who distributes the code to bind end users to terms that protect Microsoft at least as much as the license itself, and to indemnify Microsoft against claims arising from the distribution or use of the application, except claims based solely on the unmodified code. FloatDock's MIT license, with its disclaimer of warranty and limitation of liability, is the end-user license for the application; whoever republishes FloatDock builds is responsible for that obligation. This is a note, not legal advice.

## Icons and images

The application icon is drawn by `scripts/make-icons.ps1` and is part of FloatDock (MIT). The glyphs inside the dock come from the Segoe Fluent Icons / Segoe MDL2 Assets fonts that are part of Windows and are not redistributed.
