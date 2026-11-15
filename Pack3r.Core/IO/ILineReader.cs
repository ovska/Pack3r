using Pack3r.Models;

namespace Pack3r.IO;

/// <summary>
/// Reads text files as lines of ASCII bytes, see <see cref="LineRef"/>. A line is only valid until the next one
/// is read; keep what's needed with <see cref="LineRef.ToLine"/> or by converting values to strings.
/// </summary>
public interface ILineReader
{
    /// <summary>
    /// Reads the non-empty lines of a file as is, without removing comments or whitespace.
    /// </summary>
    LineEnumerable ReadRawLines(string path);

    /// <summary>
    /// Reads the lines of a file with comments and whitespace removed, skipping lines that are left empty.
    /// </summary>
    LineEnumerable ReadLines(
        string path,
        CancellationToken cancellationToken);

    /// <inheritdoc cref="ReadLines(string, CancellationToken)"/>
    LineEnumerable ReadLines(
        IAsset asset,
        CancellationToken cancellationToken);
}
