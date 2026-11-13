namespace Shotly.Localization;

/// <summary>
/// Строки интерфейса. Тексты лежат в Localization/Languages/*.json (en.json — эталон со всеми ключами),
/// здесь только типизированный доступ к ним: опечатка в имени ключа ловится компилятором,
/// а tools/check_translations.py сверяет этот список с en.json и остальными языками.
/// В подсказке у каждого свойства — английский текст.
/// </summary>
public static class L
{
    /// <summary>Shotly couldn't start:\n{0}\n\nDetails in {1}</summary>
    public static string StartupFailed(object a0, object a1) => Loc.Format(nameof(StartupFailed), a0, a1);

    /// <summary>Hotkey unavailable</summary>
    public static string HotkeyUnavailableTitle => Loc.Get();

    /// <summary>{0}. You can change it in settings.</summary>
    public static string HotkeyUnavailableText(object a0) => Loc.Format(nameof(HotkeyUnavailableText), a0);

    /// <summary>PrtScn now belongs to Shotly</summary>
    public static string PrtScnFreedTitle => Loc.Get();

    /// <summary>Windows no longer opens Snipping Tool on PrtScn. To turn it back on, go to Settings → Accessibility → Keyboard.</summary>
    public static string PrtScnFreedText => Loc.Get();

    /// <summary>PrtScn is taken by Snipping Tool and Shotly couldn't turn this off automatically. In Windows, turn off “Use the Print screen key to open screen capture”</summary>
    public static string PrtScnBlockedError => Loc.Get();

    /// <summary>PrtScn opens Snipping Tool</summary>
    public static string PrtScnBlockedTitle => Loc.Get();

    /// <summary>{0}. Click to open settings.</summary>
    public static string PrtScnBlockedText(object a0) => Loc.Format(nameof(PrtScnBlockedText), a0);

    /// <summary>Edits weren't applied</summary>
    public static string EditsLostTitle => Loc.Get();

    /// <summary>The screenshot is too large, so it was saved without annotations.</summary>
    public static string EditsLostText => Loc.Get();

    /// <summary>Screenshot copied and saved</summary>
    public static string SavedAndCopiedTitle => Loc.Get();

    /// <summary>Screenshot saved</summary>
    public static string SavedTitle => Loc.Get();

    /// <summary>Screenshot not saved</summary>
    public static string SaveFailedTitle => Loc.Get();

    /// <summary>It's still in the clipboard. {0}</summary>
    public static string SaveFailedStillCopied(object a0) => Loc.Format(nameof(SaveFailedStillCopied), a0);

    /// <summary>Clipboard is busy</summary>
    public static string ClipboardBusyTitle => Loc.Get();

    /// <summary>The screenshot wasn't copied. {0}</summary>
    public static string ClipboardBusyText(object a0) => Loc.Format(nameof(ClipboardBusyText), a0);

    /// <summary>Couldn't take a screenshot</summary>
    public static string CaptureFailedTitle => Loc.Get();

    /// <summary>Autostart wasn't changed: {0}</summary>
    public static string AutostartFailed(object a0) => Loc.Format(nameof(AutostartFailed), a0);

    /// <summary>All other settings have been saved.</summary>
    public static string OtherSettingsSaved => Loc.Get();

    /// <summary>A new version of Shotly is available</summary>
    public static string UpdateAvailableTitle => Loc.Get();

    /// <summary>Version {0}. Click to install.</summary>
    public static string UpdateAvailableText(object a0) => Loc.Format(nameof(UpdateAvailableText), a0);

    /// <summary>Couldn't check for updates.\n{0}</summary>
    public static string UpdateCheckFailed(object a0) => Loc.Format(nameof(UpdateCheckFailed), a0);

    /// <summary>You have the latest version of Shotly ({0}).</summary>
    public static string UpToDate(object a0) => Loc.Format(nameof(UpToDate), a0);

    /// <summary>Version {0} is available (you have {1}).\n\nInstall it now? Shotly will restart.</summary>
    public static string UpdateConfirm(object a0, object a1) => Loc.Format(nameof(UpdateConfirm), a0, a1);

    /// <summary>Downloading update</summary>
    public static string UpdateDownloadingTitle => Loc.Get();

    /// <summary>Couldn't install the update.\n{0}</summary>
    public static string UpdateInstallFailed(object a0) => Loc.Format(nameof(UpdateInstallFailed), a0);

    /// <summary>Shotly is still running. Close it from the tray icon (“Exit”) and try uninstalling again.</summary>
    public static string StillRunning => Loc.Get();

    /// <summary>Uninstall Shotly?\n\nThe program, its settings, autostart and its entry in the app list will be removed. Your screenshots will stay where they are.</summary>
    public static string UninstallConfirm => Loc.Get();

    /// <summary>Uninstall Shotly</summary>
    public static string UninstallTitle => Loc.Get();

    /// <summary>Shotly has been uninstalled. Your screenshots are still in this folder:\n{0}</summary>
    public static string Uninstalled(object a0) => Loc.Format(nameof(Uninstalled), a0);

    /// <summary>Delete the program file manually:\n{0}</summary>
    public static string UninstalledDeleteExe(object a0) => Loc.Format(nameof(UninstalledDeleteExe), a0);

    /// <summary>Folder unavailable</summary>
    public static string FolderUnavailableTitle => Loc.Get();

    /// <summary>“{0}”: couldn't read this shortcut</summary>
    public static string HotkeyParseError(object a0) => Loc.Format(nameof(HotkeyParseError), a0);

    /// <summary>“{0}”: this shortcut is already used by another program</summary>
    public static string HotkeyBusy(object a0) => Loc.Format(nameof(HotkeyBusy), a0);

    /// <summary>“{0}”: this is a Windows system shortcut and can't be reassigned</summary>
    public static string HotkeySystem(object a0) => Loc.Format(nameof(HotkeySystem), a0);

    /// <summary>“{0}”: this key is taken by Snipping Tool. Turn it off in Windows: Settings → Accessibility → Keyboard → “Use the Print screen key to open screen capture”</summary>
    public static string HotkeySnipping(object a0) => Loc.Format(nameof(HotkeySnipping), a0);

    /// <summary>“{0}”: this shortcut is already used by another program (often OneDrive, Lightshot or ShareX)</summary>
    public static string HotkeyBusyPrintScreen(object a0) => Loc.Format(nameof(HotkeyBusyPrintScreen), a0);

    /// <summary>Couldn't find the path to Shotly.exe.</summary>
    public static string ExePathUnknown => Loc.Get();

    /// <summary>Area {0} is off screen.</summary>
    public static string RegionOffScreen(object a0) => Loc.Format(nameof(RegionOffScreen), a0);

    /// <summary>No monitors found (is the session locked or the screen off?).</summary>
    public static string NoMonitors => Loc.Get();

    /// <summary>Couldn't allocate memory for the screenshot.</summary>
    public static string CaptureMemoryFailed => Loc.Get();

    /// <summary>Couldn't copy the screen image.</summary>
    public static string CaptureCopyFailed => Loc.Get();

    /// <summary>The update source isn't set up.</summary>
    public static string UpdateSourceMissing => Loc.Get();

    /// <summary>Can't connect to GitHub.</summary>
    public static string UpdateNoConnection => Loc.Get();

    /// <summary>GitHub didn't respond in time.</summary>
    public static string UpdateTimeout => Loc.Get();

    /// <summary>No releases found: the repository is private or has no releases yet.</summary>
    public static string UpdateNoReleases => Loc.Get();

    /// <summary>GitHub is temporarily limiting requests. Please try again later.</summary>
    public static string UpdateRateLimited => Loc.Get();

    /// <summary>GitHub returned error {0}.</summary>
    public static string UpdateHttpError(object a0) => Loc.Format(nameof(UpdateHttpError), a0);

    /// <summary>The update download timed out.</summary>
    public static string UpdateDownloadTimeout => Loc.Get();

    /// <summary>Couldn't download the update.</summary>
    public static string UpdateDownloadFailed => Loc.Get();

    /// <summary>The downloaded file is corrupted.</summary>
    public static string UpdateCorrupted => Loc.Get();

    /// <summary>Area</summary>
    public static string MenuRegion => Loc.Get();

    /// <summary>Active window</summary>
    public static string MenuWindow => Loc.Get();

    /// <summary>Full screen</summary>
    public static string MenuFullScreen => Loc.Get();

    /// <summary>Repeat last area</summary>
    public static string MenuRepeat => Loc.Get();

    /// <summary>With delay</summary>
    public static string MenuDelayed => Loc.Get();

    /// <summary>{0} s</summary>
    public static string MenuSeconds(object a0) => Loc.Format(nameof(MenuSeconds), a0);

    /// <summary>Open screenshots folder</summary>
    public static string MenuOpenFolder => Loc.Get();

    /// <summary>Settings…</summary>
    public static string MenuSettings => Loc.Get();

    /// <summary>Check for updates</summary>
    public static string MenuCheckUpdates => Loc.Get();

    /// <summary>Uninstall Shotly…</summary>
    public static string MenuUninstall => Loc.Get();

    /// <summary>Exit</summary>
    public static string MenuExit => Loc.Get();

    /// <summary>Select an area  ·  *click* — whole monitor  ·  *Esc* — cancel</summary>
    public static string HintSelect => Loc.Get();

    /// <summary>*Esc* again — close without saving</summary>
    public static string HintCloseAgain => Loc.Get();

    /// <summary>  ·  copied</summary>
    public static string SizeCopied => Loc.Get();

    /// <summary>Edit</summary>
    public static string BarEdit => Loc.Get();

    /// <summary>Arrows, boxes, text, highlighter, pixelate and blur (E)</summary>
    public static string BarEditTip => Loc.Get();

    /// <summary>Arrow (A). Hold Shift for even angles</summary>
    public static string ToolArrow => Loc.Get();

    /// <summary>Box (R). Hold Shift for a square</summary>
    public static string ToolRectangle => Loc.Get();

    /// <summary>Brush (D). Hold Shift for a straight line</summary>
    public static string ToolPen => Loc.Get();

    /// <summary>Highlighter, semi-transparent (M). Hold Shift for a straight line</summary>
    public static string ToolMarker => Loc.Get();

    /// <summary>Text (T). Drag the box to move it, drag its corners to resize</summary>
    public static string ToolText => Loc.Get();

    /// <summary>Blur area (B)</summary>
    public static string ToolBlur => Loc.Get();

    /// <summary>Blur brush (Shift+B). Hold Shift for a straight line</summary>
    public static string ToolBlurBrush => Loc.Get();

    /// <summary>Pixelate (P)</summary>
    public static string ToolPixelate => Loc.Get();

    /// <summary>Text outline is on</summary>
    public static string TextOutlineOn => Loc.Get();

    /// <summary>Text outline: a contrasting edge around the letters</summary>
    public static string TextOutlineOff => Loc.Get();

    /// <summary>Undo (Ctrl+Z)</summary>
    public static string Undo => Loc.Get();

    /// <summary>Redo (Ctrl+Y)</summary>
    public static string Redo => Loc.Get();

    /// <summary>Copy</summary>
    public static string BarCopy => Loc.Get();

    /// <summary>Copy to clipboard (Enter, Ctrl+C)</summary>
    public static string BarCopyTip => Loc.Get();

    /// <summary>Save</summary>
    public static string BarSave => Loc.Get();

    /// <summary>Save to file (Ctrl+S)</summary>
    public static string BarSaveTip => Loc.Get();

    /// <summary>Close without saving (Esc; with edits, press Esc twice)</summary>
    public static string BarClose => Loc.Get();

    /// <summary>Blur area has no thickness setting</summary>
    public static string SizeFixed => Loc.Get();

    /// <summary>Text size: drag the slider, use the mouse wheel or [ ]</summary>
    public static string SizeText => Loc.Get();

    /// <summary>Thickness: drag the slider, use the mouse wheel or [ ]</summary>
    public static string SizeThickness => Loc.Get();

    /// <summary>Custom color</summary>
    public static string CustomColor => Loc.Get();

    /// <summary>Custom color: open the palette</summary>
    public static string CustomColorOpen => Loc.Get();

    /// <summary>Custom color (click again to pick another one)</summary>
    public static string CustomColorAgain => Loc.Get();

    /// <summary>Color</summary>
    public static string Color => Loc.Get();

    /// <summary>Close</summary>
    public static string ToastClose => Loc.Get();

    /// <summary>Press a new key combination. Backspace clears it.</summary>
    public static string HotkeyBoxTip => Loc.Get();

    /// <summary>Shotly settings</summary>
    public static string SettingsTitle => Loc.Get();

    /// <summary>Settings</summary>
    public static string SettingsCaption => Loc.Get();

    /// <summary>Saving</summary>
    public static string SettingsSaving => Loc.Get();

    /// <summary>Folder</summary>
    public static string SettingsFolder => Loc.Get();

    /// <summary>Browse…</summary>
    public static string SettingsBrowse => Loc.Get();

    /// <summary>Format</summary>
    public static string SettingsFormat => Loc.Get();

    /// <summary>JPG quality</summary>
    public static string SettingsJpgQuality => Loc.Get();

    /// <summary>Window &amp; screen</summary>
    public static string SettingsQuickAction => Loc.Get();

    /// <summary>What to do with screenshots of a window, the screen and a repeated area</summary>
    public static string SettingsQuickActionTip => Loc.Get();

    /// <summary>Copy and save</summary>
    public static string QuickCopyAndSave => Loc.Get();

    /// <summary>Copy only</summary>
    public static string QuickCopy => Loc.Get();

    /// <summary>Save only</summary>
    public static string QuickSave => Loc.Get();

    /// <summary>Copy the selected area to the clipboard right away</summary>
    public static string SettingsCopyOnSelect => Loc.Get();

    /// <summary>Hotkeys</summary>
    public static string SettingsHotkeys => Loc.Get();

    /// <summary>Area</summary>
    public static string SettingsHotkeyRegion => Loc.Get();

    /// <summary>Active window</summary>
    public static string SettingsHotkeyWindow => Loc.Get();

    /// <summary>Full screen</summary>
    public static string SettingsHotkeyFullScreen => Loc.Get();

    /// <summary>Repeat area</summary>
    public static string SettingsHotkeyRepeat => Loc.Get();

    /// <summary>In Windows 11 the PrtScn key on its own opens Snipping Tool. If you assign it to Shotly, Shotly will turn off “Use the Print screen key to open screen capture” in Windows for you. </summary>
    public static string SettingsPrtScnHint => Loc.Get();

    /// <summary>Open settings</summary>
    public static string SettingsOpenWindowsSettings => Loc.Get();

    /// <summary>System</summary>
    public static string SettingsSystem => Loc.Get();

    /// <summary>Language</summary>
    public static string SettingsLanguage => Loc.Get();

    /// <summary>Same as Windows ({0})</summary>
    public static string SettingsLanguageAuto(object a0) => Loc.Format(nameof(SettingsLanguageAuto), a0);

    /// <summary>Start when I sign in to Windows</summary>
    public static string SettingsAutostart => Loc.Get();

    /// <summary>Check for updates at startup</summary>
    public static string SettingsCheckUpdates => Loc.Get();

    /// <summary>Updates</summary>
    public static string SettingsUpdates => Loc.Get();

    /// <summary>Check now</summary>
    public static string SettingsCheckNow => Loc.Get();

    /// <summary>Version {0}</summary>
    public static string SettingsVersion(object a0) => Loc.Format(nameof(SettingsVersion), a0);

    /// <summary>Save</summary>
    public static string SettingsSave => Loc.Get();

    /// <summary>Cancel</summary>
    public static string SettingsCancel => Loc.Get();

    /// <summary>Uninstall Shotly…</summary>
    public static string SettingsUninstall => Loc.Get();

    /// <summary>Screenshots folder</summary>
    public static string SettingsPickFolder => Loc.Get();

    /// <summary>Enter the full path to a folder, for example C:\Users\…\Pictures\Shotly.</summary>
    public static string SettingsFolderInvalid => Loc.Get();

    /// <summary>The shortcut {0} is assigned to two actions.</summary>
    public static string SettingsDuplicateHotkey(object a0) => Loc.Format(nameof(SettingsDuplicateHotkey), a0);
}
