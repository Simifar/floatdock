# Microsoft Store listing (text to paste into Partner Center)

Everything here is a draft written from what the app does today (the dock on every display is left out on purpose: it was only tested with a simulated second display); check each sentence against the build you submit. Screenshots are not included (take your own, at least one, 1366x768 or larger, without personal data in view).

## Product

- **Name:** FloatDock (reserve it first; if taken, add a distinguishing word only if the Store requires it)
- **Category:** Personalization (or Utilities & tools)
- **Price:** Free
- **Platforms:** Windows 10 version 1809 or later, Windows 11; PC only; x64
- **Privacy policy URL:** https://github.com/Simifar/floatdock/blob/main/PRIVACY.md
- **Website / support:** https://github.com/Simifar/floatdock , https://github.com/Simifar/floatdock/issues
- **License terms:** MIT (https://github.com/Simifar/floatdock/blob/main/LICENSE)
- **Search terms (max 7):** dock, launcher, taskbar, app launcher, window switcher, shortcuts, desktop

## Short description (up to 100 characters)

A floating dock for your desktop: running apps, window previews, pinned apps and an app menu.

## Description (English)

FloatDock puts a calm, floating dock at the bottom of your screen. It does not replace the Windows taskbar; it works next to it.

- Running apps with an indicator per window, and live window previews when you hover an app.
- Pin apps, shortcuts, folders and files; drag to reorder, drop away from the dock to unpin; import the pins of the Windows taskbar.
- An app menu with search over every installed app (also by wrong keyboard layout), a "Frequent" row and a global shortcut (Ctrl+Alt+A by default).
- Icons that grow under the pointer and push their neighbours aside; light and dark themes; English and Russian.
- Optional items: Downloads and any folder as a grid of recent files, Recycle Bin, Show desktop, clock, quick settings and notifications buttons.
- Floating, reserved (maximized windows stop above the dock) and auto-hide modes.
- Hides while a full-screen app or game is in front. No account, no ads, no telemetry.

FloatDock reads the titles and icons of your open windows to show them; nothing leaves your PC. See the privacy policy.

## Description (Russian)

FloatDock добавляет на рабочий стол спокойный плавающий док. Он не заменяет панель задач Windows, а работает рядом с ней.

- Запущенные приложения с индикатором на каждое окно и живыми превью окон при наведении.
- Закрепление приложений, ярлыков, папок и файлов; перетаскивание для порядка, бросьте значок вдали от дока, чтобы открепить; перенос закреплений из панели задач Windows.
- Меню приложений с поиском по всем установленным программам (в том числе в неверной раскладке), строка «Часто используемые» и глобальное сочетание клавиш (по умолчанию Ctrl+Alt+A).
- Значки увеличиваются под указателем и раздвигают соседей; светлая и тёмная темы; русский и английский языки.
- По желанию: «Загрузки» и любая папка сеткой последних файлов, корзина, «Показать рабочий стол», часы, кнопки быстрых настроек и уведомлений.
- Режимы: плавающий, с резервом места (развёрнутые окна останавливаются над доком) и автоскрытие.
- Скрывается, пока впереди полноэкранное приложение или игра. Без аккаунта, без рекламы, без телеметрии.

FloatDock читает заголовки и значки открытых окон, чтобы показать их; ничего не покидает ваш компьютер. Подробности в политике конфиденциальности.

## What's new (first submission)

First release in the Microsoft Store.

## Notes for certification

FloatDock is a desktop dock written with WinUI 3 and packaged with the `runFullTrust` capability. It needs full trust because it lists the open top-level windows of the user's session (EnumWindows, WinEvent hooks), registers an application desktop toolbar (AppBar) so that maximized windows stop above it, activates and closes windows on request, and starts programs the user pinned. It does not inject code into other processes, does not need administrator rights, and does not collect or send data.

This Store edition does not replace or hide the Windows taskbar and does not update itself; the optional taskbar-replacement feature of the version distributed on GitHub is disabled in this package. "Start with Windows" is off by default and uses the package's StartupTask. To test: start the app, hover or click a running app's icon, press Ctrl+Alt+A for the app menu, right-click the dock for Settings and Exit.

## Age rating answers

No violence, no sexual content, no gambling, no user-generated content, no chat, no location, no purchases, no ads. Expected rating: everyone.
