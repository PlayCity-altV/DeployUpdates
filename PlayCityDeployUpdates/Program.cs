using System.Net.Http.Headers;
using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using PlayCityDeployUpdates;

class Program
{
    public static IConfig<MainConfig> Config = new Config<MainConfig>();
    
    static async Task Main(string[] args)
    {
        var tags = await GetTags();
        if (tags is null || tags.Length < 2) return;
        
        var lastTag = tags[0];
        var lastBeforeTag = tags[1];

        var commitNames = await GetTagsDiff(lastBeforeTag, lastTag);
        
        var wc = new DiscordWebhookClient(minimumLogLevel: LogLevel.None);

        var embed = new DiscordEmbedBuilder()
            .WithColor(new DiscordColor(242, 127, 48))
            .WithTitle($"Released Play City update {lastTag}");

        string content = string.Empty;
        
        if (commitNames is null)
        {
            embed.AddField($"Updates between {lastBeforeTag} to {lastTag}:", "No changes");
        }
        else
        {
            content = commitNames.Aggregate(content, (current, name) => current + $"- {name}\n");

            embed.AddField($"Updates between {lastBeforeTag} to {lastTag}:", content);
        }

        var webHookBuilder = new DiscordWebhookBuilder()
            .AddEmbed(embed);
        
        try
        {
            await wc.AddWebhookAsync(new Uri(Config.Entries.Webhook)); 
            await wc.BroadcastMessageAsync(webHookBuilder);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    static async Task<List<string>?> GetTagsDiff(string lastBeforeTag, string lastTag)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            client.DefaultRequestHeaders.Add("User-Agent", "PlayCity");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Config.Entries.AuthToken);

            string apiUrl = $"https://api.github.com/repos/PlayCity-altV/Core/compare/{lastBeforeTag}...{lastTag}";
            HttpResponseMessage response = await client.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode) return null;
            
            string responseBody = await response.Content.ReadAsStringAsync();

            if (JsonSerializer.Deserialize<JsonDocument>(responseBody) is not {} responseJson) return null;
            
            var commits = responseJson.RootElement.GetProperty("commits");
            if (commits.GetArrayLength() < 1) return null;

            List<string> commitNames = new();

            foreach (var commit in commits.EnumerateArray())
            {
                var commitName = commit.GetProperty("commit").GetProperty("message").GetString();
                if (commitName is null) continue;
                commitNames.Add(commitName);
            }
            
            return commitNames;
        } 
    }

    static async Task<string[]?> GetTags()
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            client.DefaultRequestHeaders.Add("User-Agent", "PlayCity");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.Entries.AuthToken);

            string apiUrl = "https://api.github.com/repos/PlayCity-altV/Core/tags";
            HttpResponseMessage response = await client.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode) return null;
            
            string responseBody = await response.Content.ReadAsStringAsync();

            if (JsonSerializer.Deserialize<JsonDocument>(responseBody) is not { } responseJson) return null;
            
            var responseArray = responseJson.RootElement.EnumerateArray().ToArray();
            if (responseArray.Length < 2) return null;

            return responseArray.Select(p => p.GetProperty("name").GetString()).ToArray()!;
        }
    }
}