﻿using System.Net.Http.Headers;
using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using PlayCityDeployUpdates;

class Program
{
    public static IConfig<MainConfig> Config = new Config<MainConfig>();

    static async Task Main()
    {
        var tags = await GetTags();
        if (tags is null || tags.Length < 2) return;

        var lastTag = tags[0];
        var lastBeforeTag = tags[1];
        var commitNames = await GetTagsDiff(lastBeforeTag, lastTag);
        string content = string.Empty;

        List<string> messages = new();

        if (commitNames is null)
        {
            await BuildEmbeds(messages, lastTag, lastBeforeTag);
            return;
        }
        
        foreach (var name in commitNames)
        {
            content += $"- {name}\n";

            if (content.Length < 4000) continue;

            messages.Add(content);
            content = string.Empty;
        }

        if (messages.Count <= 0) messages.Add(content);

        await BuildEmbeds(messages, lastTag, lastBeforeTag);
    }

    static async Task BuildEmbeds(List<string> messages, string lastTag, string lastBeforeTag)
    {
        var embed = new DiscordEmbedBuilder()
            .WithColor(new DiscordColor(242, 127, 48))
            .WithThumbnail(
                "https://media.discordapp.net/attachments/1105608698400882688/1105608728495018025/logo_no_bg.png");

        if (messages.Count is 1 or <= 0)
        {
            embed.WithTitle($"Released Play City update {lastTag}");
            embed.WithDescription(
                $"Updates between {lastBeforeTag} to {lastTag}:\n{(messages.Count == 1 ? messages[0] : "No changes")}");

            await SendWebhook(embed);
        }
        else if (messages.Count > 1)
        {
            embed.WithTitle($"Released Play City update {lastTag}");
            embed.WithDescription($"Updates between {lastBeforeTag} to {lastTag}:\n{messages[0]}");

            await SendWebhook(embed);
            messages.RemoveAt(0);

            embed.WithTitle("");

            foreach (var message in messages)
            {
                embed.WithDescription(message + "\n");
                await SendWebhook(embed);
            }
        }
    }

    static async Task SendWebhook(DiscordEmbedBuilder embed)
    {
        var webhookClient = new DiscordWebhookClient(minimumLogLevel: LogLevel.None);
        var webHookBuilder = new DiscordWebhookBuilder()
            .AddEmbed(embed);

        try
        {
            await webhookClient.AddWebhookAsync(new Uri(Config.Entries.Webhook));
            await webhookClient.BroadcastMessageAsync(webHookBuilder);
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

            if (JsonSerializer.Deserialize<JsonDocument>(responseBody) is not { } responseJson) return null;

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
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Config.Entries.AuthToken);

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