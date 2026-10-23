namespace UsbDisplay.Core;

/// <summary>
/// The stop request scripts/install-tray.ps1 writes to ask the running tray to finish its frame and exit.
/// A request written before this process started was meant for an earlier instance and is ignored, so a
/// leftover file never stops a new tray and a request for this tray is never thrown away.
/// </summary>
public static class StopRequest
{
    public static bool IsFor(string path, DateTime processStartUtc) =>
        File.Exists(path) && File.GetLastWriteTimeUtc(path) >= processStartUtc;
}
