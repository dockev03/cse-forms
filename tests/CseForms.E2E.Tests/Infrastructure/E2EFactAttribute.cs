namespace CseForms.E2E.Tests.Infrastructure;

/// <summary>
/// Test de bout en bout : ignoré tant que <c>CSEFORMS_E2E=1</c> n'est pas défini, pour que
/// <c>dotnet test</c> ne demande ni navigateur ni Gotenberg sur un poste qui n'en a pas.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EFactAttribute : FactAttribute
{
    public const string EnableVariable = "CSEFORMS_E2E";

    public static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    public E2EFactAttribute()
    {
        if (!Enabled)
        {
            Skip = $"Tests de bout en bout désactivés : définir {EnableVariable}=1.";
        }
    }
}
