using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CseForms.Testing;

/// <summary>
/// Serveur SMTP minimal en mémoire, sans chiffrement ni authentification, pour vérifier les emails
/// réellement envoyés. Il conserve l'enveloppe et le message brut de chaque envoi.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ConcurrentQueue<ReceivedEmail> Received { get; } = new();

    /// <summary>Réponse de refus à la commande RCPT : simule un serveur qui rejette les envois.</summary>
    public bool RejectRecipients { get; set; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 fake.example.test ESMTP");
        string? from = null;
        var to = new List<string>();

        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake.example.test");
                    await writer.WriteLineAsync("250 8BITMIME");
                    break;
                case "HELO":
                    await writer.WriteLineAsync("250 fake.example.test");
                    break;
                case "MAIL":
                    from = Address(line);
                    to.Clear();
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "RCPT":
                    if (RejectRecipients)
                    {
                        await writer.WriteLineAsync("550 Destinataire refusé");
                        break;
                    }

                    to.Add(Address(line));
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 Fin par une ligne contenant un point");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
                    }

                    Received.Enqueue(new ReceivedEmail(from ?? string.Empty, [.. to], data.ToString()));
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Au revoir");
                    return;
                default:
                    await writer.WriteLineAsync("250 OK");
                    break;
            }
        }
    }

    private static string Address(string line)
    {
        var start = line.IndexOf('<', StringComparison.Ordinal);
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return start >= 0 && end > start ? line[(start + 1)..end] : string.Empty;
    }
}

/// <summary>Email reçu : enveloppe SMTP et message brut (en-têtes et corps).</summary>
public sealed record ReceivedEmail(string From, IReadOnlyList<string> To, string Raw);
