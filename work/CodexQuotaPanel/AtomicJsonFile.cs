using System.Text;

namespace CodexQuotaPanel;

/// <summary>
/// Small same-directory atomic writer shared by the app's local JSON stores.
/// The lock prevents the UI's independent persistence paths from competing for
/// the same destination, while the optional backup keeps the last known-good
/// settings file available after an interrupted write.
/// </summary>
internal static class AtomicJsonFile
{
    private static readonly object Gate = new();

    internal static bool TryWrite(string path, string contents, bool createBackup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        lock (Gate)
        {
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory)) return false;
                Directory.CreateDirectory(directory);

                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 4096,
                           FileOptions.WriteThrough))
                using (var writer = new StreamWriter(
                           stream,
                           new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                           bufferSize: 4096,
                           leaveOpen: true))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (!File.Exists(path))
                {
                    File.Move(temporaryPath, path);
                    return true;
                }

                if (createBackup)
                {
                    var backupPath = path + ".bak";
                    try
                    {
                        File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or
                                               UnauthorizedAccessException)
                    {
                        // Some file systems do not support ReplaceFile. Preserve a
                        // best-effort backup before using the same-volume rename path.
                        File.Copy(path, backupPath, overwrite: true);
                    }
                }

                File.Move(temporaryPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Security.SecurityException or NotSupportedException or
                                       ArgumentException)
            {
                return false;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
                {
                }
            }
        }
    }
}
