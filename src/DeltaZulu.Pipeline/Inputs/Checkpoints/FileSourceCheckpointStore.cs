using System.Text;

namespace DeltaZulu.Pipeline.Inputs.Checkpoints;

/// <summary>
/// File-backed <see cref="ISourceCheckpointStore"/> that stores one small file per source key under
/// a directory. Writes are atomic (write to a temporary file, then replace) so a crash mid-write
/// cannot leave a torn checkpoint; a missing or unreadable checkpoint is treated as absent.
/// </summary>
/// <remarks>
/// Operators should ACL-protect the checkpoint directory against local tampering: a forged bookmark
/// can cause gaps or duplicates after restart. The store never throws from <see cref="TryLoad"/> or
/// <see cref="Save"/>; failures are reported via the return value / a swallowed save so a transient
/// filesystem problem cannot terminate collection.
/// </remarks>
public sealed class FileSourceCheckpointStore : ISourceCheckpointStore
{
    private readonly string _directory;

    public FileSourceCheckpointStore(string directory) => _directory = directory;

    public void Save(string sourceKey, string token)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(sourceKey);
            var temp = path + ".tmp";
            File.WriteAllText(temp, token, Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public bool TryLoad(string sourceKey, out string token)
    {
        token = string.Empty;
        try
        {
            var path = PathFor(sourceKey);
            if (!File.Exists(path))
            {
                return false;
            }

            var content = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            token = content;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Sanitize(string sourceKey)
    {
        var builder = new StringBuilder(sourceKey.Length);
        foreach (var c in sourceKey)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private string PathFor(string sourceKey) => Path.Combine(_directory, Sanitize(sourceKey) + ".checkpoint");
}
