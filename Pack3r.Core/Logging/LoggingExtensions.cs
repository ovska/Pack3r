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
            var d = (int)delta.TotalDays;
            var h = delta.Hours;
            logger.Warn($"{type} mismatch with BSP timestamp by {d}d {h}h, ensure some files aren't from an older compile");
        }

        return isStale;
    }
}
