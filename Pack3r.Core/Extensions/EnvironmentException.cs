namespace Pack3r.Extensions;

#pragma warning disable RCS1194 // Implement exception constructors
public sealed class EnvironmentException(string message) : Exception(message);
