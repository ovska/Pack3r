using Pack3r.Models;

namespace Pack3r.IO;

/// <summary>
/// Reads text files line by line. Synchronous: the parsers run on thread pool threads in parallel loops, so
/// async reads would only add overhead per line.
/// </summary>
public interface ILineReader
{
    /// <summary>
    /// Reads the non-empty lines of a file as is, without removing comments or whitespace.
    /// </summary>
    IEnumerable<Line> ReadRawLines(string path);

    /// <summary>
    /// Reads the lines of a file with comments and whitespace removed, skipping lines that are left empty.
    /// </summary>
    IEnumerable<Line> ReadLines(
        string path,
        CancellationToken cancellationToken);

    /// <inheritdoc cref="ReadLines(string, CancellationToken)"/>
    IEnumerable<Line> ReadLines(
        IAsset asset,
        CancellationToken cancellationToken);
}
