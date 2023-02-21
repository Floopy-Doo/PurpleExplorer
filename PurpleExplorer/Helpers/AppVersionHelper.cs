using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace PurpleExplorer.Helpers;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class AppVersionHelper
{
    public static async Task<GithubRelease> GetLatestRelease()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Application");
        var response =
            await httpClient.GetAsync(
                "https://api.github.com/repos/telstrapurple/PurpleExplorer/releases/latest");
        var content = await response.Content.ReadAsStringAsync();
        var githubRelease = JsonSerializer.Deserialize<GithubRelease>(content);
        return githubRelease;
    }
}

public class GithubRelease
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("url")]
    public string Url { get; set; }
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; } 
}