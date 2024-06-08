namespace Pack3r.Extensions;

/// <summary>
/// Exception for invalid mapping environment.
/// </summary>
public sealed class EnvironmentException(string message) : Exception(message);
