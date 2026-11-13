using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CseForms.Testing;

namespace CseForms.E2E.Tests.Infrastructure;

/// <summary>Fiches d'inscription fictives envoyées directement à l'API publique, pour préparer les scénarios du back-office.</summary>
public static class FictiveSubmissions
{
    /// <summary>Envoie une fiche en CDI (la date de fin est masquée) et renvoie son numéro de dossier.</summary>
    public static async Task<string> SubmitCdiAsync(E2EEnvironment app, string nom = "Exemple")
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(app.BaseUrl) };
        var form = await http.GetFromJsonAsync<JsonObject>($"/api/public/forms/{E2EEnvironment.FormSlug}");

        var payload = new JsonObject
        {
            ["version"] = form!["version"]!.GetValue<int>(),
            ["accessCode"] = app.AccessCode,
            ["data"] = new JsonObject
            {
                ["civilite"] = "M.",
                ["nom"] = nom,
                ["prenom"] = "camille",
                ["contrat"] = "CDI",
                ["dateEmbauche"] = "2024-03-04",
                ["site"] = "Abrest",
                ["adresse"] = new JsonObject { ["street"] = "1 rue de l'Exemple", ["postalCode"] = "63200", ["city"] = "Riom" },
                ["emailPerso"] = "camille.exemple@example.test",
                ["emailConfirmation"] = "camille.exemple@example.test",
                ["enfants"] = new JsonArray(new JsonObject
                {
                    ["nom"] = nom, ["prenom"] = "lou", ["naissance"] = "2020-06-15", ["sexe"] = "F",
                }),
                ["rgpdLu"] = true,
                ["attestation"] = true,
            },
        };

        var response = await http.PostWithProofAsync($"/api/public/forms/{E2EEnvironment.FormSlug}/submissions", payload);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return response.IsSuccessStatusCode
            ? body!["caseNumber"]!.GetValue<string>()
            : throw new InvalidOperationException($"Soumission refusée ({(int)response.StatusCode}) : {body}");
    }
}
