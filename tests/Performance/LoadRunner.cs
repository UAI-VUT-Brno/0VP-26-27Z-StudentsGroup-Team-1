using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UAI.ZeroVP.Tests.Performance;

/// <summary>Jeden změřený HTTP požadavek.</summary>
/// <param name="Endpoint">Šablona cesty, např. <c>GET /api/todos/{id}</c> – podle ní se vzorky seskupují.</param>
/// <param name="StatusCode">Stavový kód odpovědi.</param>
/// <param name="Duration">Doba od odeslání požadavku po přečtení celého těla odpovědi.</param>
internal sealed record RequestSample(string Endpoint, HttpStatusCode StatusCode, TimeSpan Duration);

/// <summary>
/// Obal nad <see cref="HttpClient"/>, který u každého požadavku změří dobu odezvy
/// a uloží ji do sdíleného seznamu vzorků.
/// </summary>
internal sealed class TimedHttpClient(HttpClient client, ConcurrentBag<RequestSample> samples)
{
    /// <summary>Stejné nastavení JSON jako v API – výčty se posílají jako řetězce.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Odešle požadavek, změří ho a vrátí tělo odpovědi.</summary>
    public async Task<string> SendAsync(string endpoint, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json)
        };

        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);

        samples.Add(new RequestSample(endpoint, response.StatusCode, elapsed));
        return content;
    }
}

/// <summary>
/// Jednoduchý generátor zátěže: spustí zadaný počet souběžných „uživatelů“
/// a každý z nich několikrát po sobě projde scénář.
/// </summary>
internal static class LoadRunner
{
    public static async Task<IReadOnlyList<RequestSample>> RunAsync(
        HttpClient client,
        int users,
        int iterationsPerUser,
        Func<TimedHttpClient, Task> scenario)
    {
        var samples = new ConcurrentBag<RequestSample>();
        var timedClient = new TimedHttpClient(client, samples);
        // Task.Run spustí každého uživatele samostatně na thread poolu, takže všichni začnou současně.
        // Volá se jednou na uživatele (50×), ne na požadavek, a mimo měřený úsek – doba odezvy
        // se tím nezkresluje. Měření bez Task.Run dalo stejné výsledky.
        var userTasks = Enumerable.Range(0, users).Select(_ => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < iterationsPerUser; iteration++)
            {
                await scenario(timedClient);
            }
        }));

        await Task.WhenAll(userTasks);
        return [.. samples];
    }
}