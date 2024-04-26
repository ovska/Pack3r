namespace Pack3r;

public static class LoggingExtensions
{
    public static void UnparsableKeyword(
        this ILogger logger,
        string path,
        int index,
        string prefix,
        string raw)
    {
        logger.Warn($"Unparsable keyword '{prefix}' on line {index} in file {path}: {raw}");
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
            var readable = delta.TotalDays < 1 ? $"{delta:%h} hours+" : $"{delta:%d} day(s)+";
            logger.Warn(
                $"{type} file(s) have different timestamps than BSP ({readable}), ensure they are from a recent compile");
        }

        return isStale;
    }
}
