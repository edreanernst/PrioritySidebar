using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PrioritySidebar;

public sealed record TrelloLabel(string Id, string? Name, string? Color);
public sealed record TrelloBadges(int CheckItems, int CheckItemsChecked);
public sealed record TrelloCard(string Id, string Name, double Pos, string? ShortUrl, string? IdBoard, List<TrelloLabel>? Labels,
    string? Desc = null, TrelloBadges? Badges = null);
public sealed record TrelloCheckItem(string Id, string Name, string? State, double Pos);
public sealed record TrelloChecklist(string Id, string Name, double Pos, List<TrelloCheckItem>? CheckItems);
public sealed record TrelloNamed(string Id, string Name);
public sealed record TrelloMember(string Id, string? FullName, string? Username);

public sealed class TrelloException(HttpStatusCode status, string body)
    : Exception($"Trello returned {(int)status}: {body}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>The handful of Trello REST calls the sidebar needs.</summary>
public sealed class TrelloClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public TrelloClient(string apiKey, string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.trello.com/1/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        // Header auth keeps the key and token out of request URLs.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "OAuth", $"oauth_consumer_key=\"{apiKey}\", oauth_token=\"{token}\"");
    }

    public Task<TrelloMember> GetMe() =>
        Send<TrelloMember>(HttpMethod.Get, "members/me?fields=fullName,username");

    public Task<List<TrelloNamed>> GetBoards() =>
        Send<List<TrelloNamed>>(HttpMethod.Get, "members/me/boards?filter=open&fields=name");

    public Task<List<TrelloNamed>> GetLists(string boardId) =>
        Send<List<TrelloNamed>>(HttpMethod.Get, $"boards/{boardId}/lists?filter=open&fields=name");

    // The badges carry each card's checklist counts, so the full checklists only load when a card is opened.
    public Task<List<TrelloCard>> GetCards(string listId) =>
        Send<List<TrelloCard>>(HttpMethod.Get, $"lists/{listId}/cards?fields=name,pos,shortUrl,idBoard,labels,desc,badges");

    public Task<List<TrelloChecklist>> GetChecklists(string cardId) =>
        Send<List<TrelloChecklist>>(HttpMethod.Get,
            $"cards/{cardId}/checklists?fields=name,pos&checkItems=all&checkItem_fields=name,state,pos");

    public Task SetCheckItem(string cardId, string checkItemId, bool done) =>
        Send(HttpMethod.Put, $"cards/{cardId}/checkItem/{checkItemId}?state={(done ? "complete" : "incomplete")}");

    /// <param name="pos">"top", "bottom" or a number from <see cref="Ranking"/>.</param>
    public Task<TrelloCard> MoveCard(string cardId, string pos) =>
        Send<TrelloCard>(HttpMethod.Put, $"cards/{cardId}?pos={pos}");

    public Task<TrelloCard> AddCard(string listId, string name) =>
        Send<TrelloCard>(HttpMethod.Post, $"cards?idList={listId}&pos=bottom&name={Uri.EscapeDataString(name)}");

    public Task<TrelloCard> SetArchived(string cardId, bool archived, string? pos = null) =>
        Send<TrelloCard>(HttpMethod.Put,
            $"cards/{cardId}?closed={(archived ? "true" : "false")}" + (pos is null ? "" : $"&pos={pos}"));

    /// <summary>Finds the board's "Done" label, creating a green one if it does not exist yet.</summary>
    public async Task<TrelloLabel> GetOrCreateDoneLabel(string boardId)
    {
        var labels = await Send<List<TrelloLabel>>(HttpMethod.Get, $"boards/{boardId}/labels?fields=name,color&limit=1000");
        return labels.FirstOrDefault(l => string.Equals(l.Name?.Trim(), "Done", StringComparison.OrdinalIgnoreCase))
            ?? await Send<TrelloLabel>(HttpMethod.Post, $"labels?idBoard={boardId}&name=Done&color=green");
    }

    public async Task AddLabel(string cardId, string labelId)
    {
        try
        {
            await Send(HttpMethod.Post, $"cards/{cardId}/idLabels?value={labelId}");
        }
        catch (TrelloException e) when (e.Status == HttpStatusCode.BadRequest)
        {
            // Trello answers 400 when the card already has the label.
        }
    }

    public Task RemoveLabel(string cardId, string labelId) =>
        Send(HttpMethod.Delete, $"cards/{cardId}/idLabels/{labelId}");

    public void Dispose() => _http.Dispose();

    private async Task<T> Send<T>(HttpMethod method, string path)
    {
        using var response = await SendRaw(method, path);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task Send(HttpMethod method, string path)
    {
        using var response = await SendRaw(method, path);
    }

    private async Task<HttpResponseMessage> SendRaw(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.Dispose();
            throw new TrelloException(response.StatusCode, body);
        }
        return response;
    }
}

/// <summary>Trello orders cards by a hidden "pos" number; a moved card gets one between its new neighbours.</summary>
public static class Ranking
{
    public static string PosBetween(double? above, double? below) => (above, below) switch
    {
        (null, _) => "top",
        (_, null) => "bottom",
        ({ } a, { } b) => ((a + b) / 2).ToString("R", CultureInfo.InvariantCulture),
    };
}
