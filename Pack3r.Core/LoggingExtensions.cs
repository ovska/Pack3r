using Microsoft.Extensions.Logging;

namespace Pack3r;

public static partial class LoggingExtensions
{
    [LoggerMessage(
        LogLevel.Debug,
        "Shader '{shader}' in file '{path}' line {index}: matched {prefix} with value: {value}")]
    public static partial void MatchedShader(
        this ILogger logger,
        string path,
        int index,
        ReadOnlyMemory<char> shader,
        string prefix,
        ReadOnlyMemory<char> value);

    [LoggerMessage(
        LogLevel.Critical,
        "Expected shader name on line {index} in file '{path}', got: '{actual}'")]
    public static partial void ExpectedShader(
        this ILogger logger,
        string path,
        int index,
        string actual);

    [LoggerMessage(
        LogLevel.Critical,
        "Expected {{ on line {index} in file '{path}', got: '{actual}'")]
    public static partial void ExpectedOpeningBrace(
        this ILogger logger,
        string path,
        int index,
        string actual);

    [LoggerMessage(
        LogLevel.Critical,
        "Invalid token {value} on line {index} in file {path}")]
    public static partial void InvalidToken(
        this ILogger logger,
        string path,
        int index,
        string value);

    [LoggerMessage(
        LogLevel.Warning,
        "Unparsable {prefix} keyword on line {index} in file {path}: {raw}")]
    public static partial void UnparsableKeyword(
        this ILogger logger,
        string path,
        int index,
        string prefix,
        string raw);
}
