# Stage 7 follow-up check: a dock on every display, and one AppBar journal entry per dock window.
# A machine with one monitor has no second display, so the app is started with --fake-display (test only): a rectangle
# that counts as a second display. The real multi-monitor paths (plugging a monitor in, other scales) stay a manual check.
# Needs pwsh and a visible desktop. Backs up settings.json and startup-attempts.json into a folder and verifies the restore.
# Close FloatDock first. The Reserved part briefly shrinks the real work area and always puts it back.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, System.Windows.Forms
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class DC {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint a, uint p, out Rect r, uint f);
  public static int WorkAreaBottom() { Rect r; SystemParametersInfoW(0x30, 0, out r, 0); return r.Bottom; }
  // "handle left top right bottom toolwindow" of every visible top-level window of the process with this title.
  public static string[] Windows(int pid, string title) {
    var found = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var s = new StringBuilder(256); GetWindowTextW(h, s, 256);
      if (s.ToString() != title) return true;
      Rect r; GetWindowRect(h, out r);
      long ex = GetWindowLongPtr(h, -20).ToInt64();
      found.Add(h.ToInt64() + " " + r.Left + " " + r.Top + " " + r.Right + " " + r.Bottom + " " + (((ex & 0x80) != 0) ? 1 : 0));
      return true; }, IntPtr.Zero);
    return found.ToArray(); }
}
'@
if (Get-Process FloatDock.App -ErrorAction SilentlyContinue) { throw 'Close FloatDock first.' }
$A = [Windows.Automation.AutomationElement]
$dir = Join-Path $env:LOCALAPPDATA 'FloatDock'
$settings = Join-Path $dir 'settings.json'; $attempts = Join-Path $dir 'startup-attempts.json'; $journal = Join-Path $dir 'appbar.json'
$backup = Join-Path $env:TEMP ('fd-' + [guid]::NewGuid()); New-Item -ItemType Directory $backup | Out-Null
foreach ($f in $settings, $attempts) { if (Test-Path $f) { Copy-Item $f $backup } }
$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { "{0}: {1} {2}" -f $name, $(if ($ok) { 'ok' } else { 'FAILED' }), $detail; if (-not $ok) { $script:failures += $name } }
function Wait-Until([scriptblock]$c, [int]$ms = 15000) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { if (& $c) { return $true }; Start-Sleep -Milliseconds 100 }; $false }
function Docks($p) { @([DC]::Windows($p.Id, 'FloatDock') | ForEach-Object { $t = $_ -split ' '; [pscustomobject]@{ H = [int64]$t[0]; L = [int]$t[1]; T = [int]$t[2]; R = [int]$t[3]; B = [int]$t[4]; Tool = $t[5] -eq '1' } }) }
function Start-Dock([string]$json, [string[]]$arguments) {
  Remove-Item $attempts -ErrorAction SilentlyContinue
  $json | Set-Content $settings
  Start-Process $Exe -ArgumentList $arguments -PassThru
}
function Quit-Dock($p) {
  $e = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); [void]$e.Set(); $e.Dispose()
  Check 'Exits after the quit signal' ($p.WaitForExit(8000))
}
function Journal { if (Test-Path $journal) { @(Get-Content $journal -Raw | ConvertFrom-Json) } else { @() } }
function Find-Window([string]$name, $pid_) { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.AndCondition(
  (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $pid_)), (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))))) }
$primary = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$p = $null
try {
  # --- Part A: Floating, a fake second display inside the primary one --------------------------------------------------
  $fake = @(0, 0, 1280, 720)
  $p = Start-Dock '{"FirstRunCompleted":true,"Mode":0,"Displays":1,"Language":1}' @("--fake-display=$($fake -join ',')")
  Check 'Two docks with Displays=All' (Wait-Until { (Docks $p).Count -eq 2 })
  Start-Sleep -Seconds 2
  $docks = Docks $p
  $onFake = @($docks | Where-Object { $_.B -le 720 })
  $onMain = @($docks | Where-Object { $_.B -gt 720 })
  Check 'One dock is inside the fake display and one on the primary one' ($onFake.Count -eq 1 -and $onMain.Count -eq 1) (($docks | ForEach-Object { "[$($_.L),$($_.T),$($_.R),$($_.B)]" }) -join ' ')
  if ($onFake.Count -eq 1 -and $onMain.Count -eq 1) {
    $f = $onFake[0]; $m = $onMain[0]
    Check 'Fake dock is centred on the fake display' ([math]::Abs((($f.L + $f.R) / 2) - 640) -le 2) "centre=$(($f.L + $f.R) / 2)"
    Check 'Fake dock sits above the fake display bottom' ($f.B -le 720 -and $f.B -ge 720 - 40) "bottom=$($f.B)"
    Check 'Primary dock is centred on the primary display' ([math]::Abs((($m.L + $m.R) / 2) - ($primary.Left + $primary.Width / 2)) -le 2)
    Check 'Both are tool windows (no Alt+Tab entry)' ($f.Tool -and $m.Tool)
  }
  $buttons = $A::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
    (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)), (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'FloatDock.apps')))))
  Check 'Each dock has its own Apps button' ($buttons.Count -eq 2) "count=$($buttons.Count)"
  # The Apps button of the fake dock opens the menu over the fake display.
  $fakeButton = $buttons | Where-Object { $_.Current.BoundingRectangle.Bottom -le 720 } | Select-Object -First 1
  if ($fakeButton) {
    $fakeButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Check 'The app menu opens' (Wait-Until { (@([DC]::Windows($p.Id, 'FloatDock Apps'))).Count -eq 1 } 6000)
    $menu = @([DC]::Windows($p.Id, 'FloatDock Apps')) | Select-Object -First 1
    if ($menu) { $t = $menu -split ' '; Check 'The app menu covers the fake display, not the primary one' ([int]$t[3] -le 1280 + 2 -and [int]$t[4] -le 720 + 2) "rect=$($t[1]),$($t[2]),$($t[3]),$($t[4])" }
    [Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}'); Start-Sleep -Milliseconds 600
  }

  # Switching the setting in the Settings window creates and closes the extra dock at run time.
  $activate = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Activate'); [void]$activate.Set(); $activate.Dispose()
  $settingsReady = Wait-Until { $null -ne (Find-Window 'FloatDock Settings' $p.Id) } 8000
  Check 'Settings window opens' $settingsReady
  if ($settingsReady) {
    $win = Find-Window 'FloatDock Settings' $p.Id
    # The displays choice is on the Dock page of the settings window.
    $page = $win.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Dock')))
    if ($page) { $page.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 800 }
    # The row's title and its combo box share the name; only the combo box can expand.
    function Find-Combo($w) { $w.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
      (New-Object Windows.Automation.PropertyCondition($A::NameProperty, 'Show the dock on')),
      (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::ComboBox))))) }
    $box = Find-Combo $win
    Check 'Displays setting is listed' ($null -ne $box)
    if ($box) {
      function Choose($box, [string]$item) {
        $box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
        $entry = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $item)))
        $entry.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
      }
      Choose $box 'The main display only'
      Check 'The extra dock closes when only the primary display is wanted' (Wait-Until { (Docks $p).Count -eq 1 } 8000)
      Check 'The choice is saved' ((Get-Content $settings -Raw | ConvertFrom-Json).Displays -eq 0)
      $box = Find-Combo (Find-Window 'FloatDock Settings' $p.Id)
      Choose $box 'Every display'
      Check 'The extra dock comes back when every display is wanted' (Wait-Until { (Docks $p).Count -eq 2 } 8000)
      Check 'The choice is saved again' ((Get-Content $settings -Raw | ConvertFrom-Json).Displays -eq 1)
    }
  }
  Quit-Dock $p
  Check 'No dock window is left after exit' ((Docks $p).Count -eq 0)

  # --- Part B: Reserved, a fake display equal to the primary one: two AppBar bands, two journal entries ------------------
  $originalBottom = [DC]::WorkAreaBottom()
  $full = @($primary.Left, $primary.Top, $primary.Width, $primary.Height)
  $p = Start-Dock '{"FirstRunCompleted":true,"Mode":1,"Displays":1,"Language":1}' @("--fake-display=$($full -join ',')")
  Check 'Two journal entries for two docks' (Wait-Until { (Journal).Count -eq 2 } 12000) "entries=$((Journal).Count)"
  $entries = Journal
  Check 'Entries name different windows of this process' (($entries | Select-Object -ExpandProperty Window -Unique).Count -eq 2 -and @($entries | Where-Object { $_.ProcessId -ne $p.Id }).Count -eq 0)
  Check 'The two bands shrink the work area more than one would' ([DC]::WorkAreaBottom() -lt $originalBottom - 100) "bottom=$([DC]::WorkAreaBottom()) was $originalBottom"
  Quit-Dock $p
  Check 'Both bands are released on a normal exit' (Wait-Until { -not (Test-Path $journal) -and [DC]::WorkAreaBottom() -eq $originalBottom } 5000) "bottom=$([DC]::WorkAreaBottom())"

  # A kill leaves both registrations behind; --restore-taskbar removes both.
  $p = Start-Dock '{"FirstRunCompleted":true,"Mode":1,"Displays":1,"Language":1}' @("--fake-display=$($full -join ',')")
  [void](Wait-Until { (Journal).Count -eq 2 } 12000)
  Stop-Process -Id $p.Id -Force; Start-Sleep -Seconds 1
  Check 'A killed dock leaves its two registrations in the journal' ((Journal).Count -eq 2)
  Start-Process $Exe -ArgumentList '--restore-taskbar' -Wait
  Check 'The restore command removes both and the work area is back' (Wait-Until { -not (Test-Path $journal) -and [DC]::WorkAreaBottom() -eq $originalBottom } 5000) "bottom=$([DC]::WorkAreaBottom())"
}
finally {
  Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  Start-Process $Exe -ArgumentList '--restore-taskbar' -Wait -ErrorAction SilentlyContinue
  foreach ($f in $settings, $attempts) {
    $saved = Join-Path $backup (Split-Path $f -Leaf)
    if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
  }
  $restored = $true
  foreach ($saved in Get-ChildItem $backup) {
    if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $dir $saved.Name)).Hash) { $restored = $false }
  }
  if ($restored) { Remove-Item $backup -Recurse } else { "RESTORE FAILED, the copies are in $backup" }
  "Restored: $restored"
}
if ($failures.Count -gt 0) { throw ('Failed: ' + ($failures -join '; ')) }
