namespace CseForms.Domain.Common;

/// <summary>
/// Violation d'une règle métier. Le message est destiné aux journaux techniques,
/// jamais renvoyé tel quel au client.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
