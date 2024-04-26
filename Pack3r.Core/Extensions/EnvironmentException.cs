namespace Pack3r.Extensions;

#pragma warning disable RCS1194 // Implement exception constructors

/// <summary>
/// Exception for invalid mapping environment.
/// </summary>
public sealed class EnvironmentException(string message) : Exception(message);
