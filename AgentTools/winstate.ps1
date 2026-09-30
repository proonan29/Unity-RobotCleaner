# Report (and optionally restore) the Unity Editor window state without stealing focus unless -Restore.
param([switch]$Restore)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
$p = Get-Process Unity | Where-Object { $_.MainWindowTitle -like "CleanerBot*" } | Select-Object -First 1
$h = $p.MainWindowHandle
"minimized=$([W]::IsIconic($h)) visible=$([W]::IsWindowVisible($h)) foreground=$([W]::GetForegroundWindow() -eq $h)"
if ($Restore -and [W]::IsIconic($h)) { [W]::ShowWindow($h, 4) | Out-Null; "restored (SW_SHOWNOACTIVATE)" }
