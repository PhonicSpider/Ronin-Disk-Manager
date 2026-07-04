using System.IO;
using System.Text;

namespace RoninDiskManager.Services;

// ── Operation log ───────────────────────────────────────────────────────────
// Appends every Move / Copy / Delete / Recycle to a plain-text audit trail under
// %AppData%\RoninDiskManager\operations.log. Fire-and-forget and best-effort:
// logging never throws into the caller and a failure to write is swallowed.
public static class OperationLog
{
    private static readonly object Gate = new();

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoninDiskManager");

    public static string FilePath { get; } = Path.Combine(Dir, "operations.log");

    /// <summary>
    /// Records one operation. <paramref name="action"/> is a short verb (e.g. "MOVE"),
    /// <paramref name="detail"/> the command or path, and <paramref name="result"/> the outcome.
    /// </summary>
    public static void Record(string action, string detail, string result)
    {
        // Format the timestamp locally so entries read in wall-clock order.
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{action}]  {detail}  =>  {result}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* auditing is best-effort */ }
    }
}
