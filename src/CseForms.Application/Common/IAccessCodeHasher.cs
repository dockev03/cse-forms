namespace CseForms.Application.Common;

/// <summary>Hachage du code d'accès d'un formulaire ; le code n'est jamais stocké en clair.</summary>
public interface IAccessCodeHasher
{
    string Hash(string accessCode);

    bool Verify(string hash, string accessCode);
}
