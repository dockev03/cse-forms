using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CseForms.Testing;

/// <summary>Résout le défi anti-robot de l'API publique comme le ferait le navigateur, pour les tests.</summary>
public static class ProofOfWorkClient
{
    /// <summary>Clé HMAC de test, jamais utilisée ailleurs.</summary>
    public static readonly string TestKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(200 - i)).ToArray());

    public static async Task<JsonObject> SolveAsync(HttpClient client)
    {
        var challenge = (await client.GetFromJsonAsync<JsonObject>("/api/public/challenge"))!;
        var salt = challenge["salt"]!.GetValue<string>();
        var expected = challenge["challenge"]!.GetValue<string>();
        var max = challenge["maxNumber"]!.GetValue<int>();

        for (var number = 0; number <= max; number++)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture))));
            if (hash == expected)
            {
                return new JsonObject
                {
                    ["algorithm"] = challenge["algorithm"]!.GetValue<string>(),
                    ["challenge"] = expected,
                    ["number"] = number,
                    ["salt"] = salt,
                    ["signature"] = challenge["signature"]!.GetValue<string>(),
                };
            }
        }

        throw new InvalidOperationException("Défi sans solution.");
    }

    /// <summary>Envoie une réponse au formulaire public avec une preuve de travail valide.</summary>
    public static async Task<HttpResponseMessage> PostWithProofAsync(this HttpClient client, string url, object body)
    {
        var payload = JsonSerializer.SerializeToNode(body) as JsonObject ?? throw new ArgumentException("Objet JSON attendu.", nameof(body));
        payload["proof"] = await SolveAsync(client);
        return await client.PostAsJsonAsync(url, payload);
    }
}
