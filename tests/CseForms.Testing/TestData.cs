using CseForms.Domain.Forms;
using CseForms.Domain.Sites;
using CseForms.Infrastructure.Persistence;
using CseForms.Infrastructure.Security;

namespace CseForms.Testing;

/// <summary>Données de test exclusivement fictives.</summary>
public static class TestData
{
    public static readonly DateTimeOffset Past = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const string SimpleDefinition = """
        {"sections":[{"id":"s","fields":[
          {"id":"nom","type":"text","label":"Nom","required":true,"transform":"upper"},
          {"id":"site","type":"site","label":"Lieu de travail","required":true}
        ]}]}
        """;

    /// <summary>Clé de chiffrement de test, jamais utilisée ailleurs.</summary>
    public static readonly string StorageKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());

    public static string HashAccessCode(string code) => new AccessCodeHasher().Hash(code);

    public static async Task<Form> AddPublishedFormAsync(
        CseFormsDbContext db,
        string slug,
        string definition = SimpleDefinition,
        Action<Form>? configure = null)
    {
        var form = new Form(slug, "Formulaire fictif", Past);
        form.SetCaseNumberPrefix("TST");
        form.Publish(definition, null, null, Past, "test");
        configure?.Invoke(form);

        db.Forms.Add(form);
        await db.SaveChangesAsync();
        return form;
    }

    public static async Task EnsureSiteAsync(CseFormsDbContext db, string label)
    {
        if (!db.Sites.Any(s => s.Label == label))
        {
            db.Sites.Add(new Site(label));
            await db.SaveChangesAsync();
        }
    }
}
