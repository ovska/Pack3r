namespace Pack3r.Logging;

public static class LoggingExtensions
{
    public static void UnparsableKeyword(
        this ILogger logger,
        string path,
        int index,
        string prefix,
        string raw)
    {
        logger.Warn($"Unparsable keyword '{prefix}' on line {index} in file '{path}': {raw}");
    }

    public static bool CheckAndLogTimestampWarning(
        this ILogger logger,
        string type,
        FileInfo bsp,
        FileInfo other)
    {
        TimeSpan delta = (other.LastWriteTimeUtc - bsp.LastWriteTimeUtc).Duration();
        bool isStale = delta > TimeSpan.FromHours(1);

        if (isStale)
        {
            var d = delta.Days;
            var h = delta.Hours;
            logger.Warn($"{type} file(s) have different timestamps than BSP ({d}d {h}h), ensure they are from a recent compile");
        }

        return isStale;
    }
}
