using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using PlayCityDeployUpdates;

class Program
{
    public static IConfig<MainConfig> Config = new Config<MainConfig>();
    public static string ProjectName = "Core";
    public static string LastPreviousTagVersion;
    public static Regex IgnoredPrefixesRE = new(@"^(Merge|Revert|Apply|Conflicts)\b");
    public static Regex CommitRE = new(@"(\w+)\(([^)]+)\):\s(.+)");
    public static int ChunkSize = 20;

    static async Task Main(string[] args)
    {
        if (args.Contains("--project"))
        {
            var argIndex = args.ToList().IndexOf("--project");
            if (argIndex + 1 >= args.Length) return;

            ProjectName = args[argIndex + 1];
        }

        var tags = await GetTags(ProjectName);
        if (tags is null || tags.Length < 2) return;

        var filePath = $"./lastPreviousTag-{ProjectName}.txt";

        var lastTag = tags[0];

        var commitNames = await GetTagsDiff(ProjectName, LastPreviousTagVersion, lastTag);

        if (commitNames is null)
        {
            return;
        }

        var commitsInfo = new Dictionary<string, (string title, List<(string scope, string message)> commits)>()
        {
            { "feat", ("Features", new()) },
            { "fix", ("Bug Fixes", new()) },
            { "chore", ("Chores", new()) },
            { "build", ("Build", new()) },
            { "ci", ("Continuous integration (CI)", new()) },
            { "refactor", ("Refactor", new()) },
            { "other", ("Commits", new()) },
        };

        commitNames.ForEach(commitName =>
        {
            if (IgnoredPrefixesRE.IsMatch(commitName))
            {
                return;
            }

            var match = CommitRE.Match(commitName);
            if (match.Success)
            {
                var type = match.Groups[1].Value;
                var scope = match.Groups[2].Value;
                var message = match.Groups[3].Value;

                if (!commitsInfo.ContainsKey(type))
                {
                    commitsInfo["other"].commits.Add(("", commitName));
                }
                else commitsInfo[type].commits.Add((scope, message));
            }
            else
            {
                commitsInfo["other"].commits.Add(("", commitName));
            }
        });

        await BuildEmbeds(commitsInfo, LastPreviousTagVersion, lastTag);
        
        if (!File.Exists(filePath))
        {
            await File.WriteAllTextAsync(filePath, lastTag);
            LastPreviousTagVersion = tags.Last();
        }
        else
        {
            LastPreviousTagVersion = await File.ReadAllTextAsync(filePath);
            await File.WriteAllTextAsync(filePath, lastTag);
        }
    }

    static async Task BuildEmbeds(Dictionary<string, (string title, List<(string scope, string message)>)> commitsInfo,
        string lastPreviousTag, string lastTag)
    {
        var embedFields = new List<(string name, string value)>();

        foreach (var (type, (title, commits)) in commitsInfo)
        {
            if (commits.Count <= 1)
            {
                continue;
            }

            embedFields.Add(($"**{title}**\n\n", ""));

            foreach (var commit in commits)
            {
                var scope = commit.scope;
                var message = commit.message;

                if (scope.Length < 1 && message.Length < 1)
                {
                    continue;
                }

                embedFields.Add(scope.Length >= 1 ? ("", $"{scope}: {message}") : ("", $"{message}"));
            }
            
            embedFields.Add(("\n", ""));
        }

        List<List<(string name, string value)>> fieldChunks = new();

        for (int i = 0; i < embedFields.Count; i += ChunkSize)
        {
            var chunk = embedFields.Skip(i).Take(ChunkSize).ToList();
            fieldChunks.Add(chunk);
        }

        var embedsBuilders = new List<DiscordEmbedBuilder>();
        var description = string.Empty;

        embedsBuilders.Add(new DiscordEmbedBuilder()
            .WithColor(new DiscordColor(242, 127, 48))
            .WithThumbnail(
                "https://media.discordapp.net/attachments/1105608698400882688/1105608728495018025/logo_no_bg.png")
            .WithTimestamp(DateTime.Now)
            .WithTitle($"Released new {ProjectName} version {lastTag}")
            .WithDescription($"Updates between {lastPreviousTag} -> {lastTag}:"));

        foreach (var (name, value) in fieldChunks[0])
        {
            description += $"{(name.Length > 0 ? name : "")}{(value.Length > 0 ? value + "\n" : "")}";
        }

        embedsBuilders[0].WithDescription(description);

        for (int i = 1; i < fieldChunks.Count; i++)
        {
            embedsBuilders.Add(new DiscordEmbedBuilder()
                .WithColor(new DiscordColor(242, 127, 48))
                .WithThumbnail(
                    "https://media.discordapp.net/attachments/1105608698400882688/1105608728495018025/logo_no_bg.png")
                .WithTimestamp(DateTime.Now));

            foreach (var (name, value) in fieldChunks[i])
            {
                description += $"{(name.Length > 0 ? name + "\n" : "")}{(value.Length > 0 ? value + "\n" : "")}";
            }

            embedsBuilders[i].WithDescription(description + "\n");
        }

        var embeds = new List<DiscordEmbed>();
        embedsBuilders.ForEach(embedsBuilder => embeds.Add(embedsBuilder.Build()));

        await SendWebhook(embeds);
    }

    static async Task SendWebhook(IEnumerable<DiscordEmbed> embeds)
    {
        var webhookClient = new DiscordWebhookClient(minimumLogLevel: LogLevel.None);
        var webHookBuilder = new DiscordWebhookBuilder()
            .AddEmbeds(embeds);

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

    static async Task<List<string>?> GetTagsDiff(string projectName, string lastPreviousTag, string lastTag)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            client.DefaultRequestHeaders.Add("User-Agent", "PlayCity");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Config.Entries.AuthToken);

            string apiUrl =
                $"https://api.github.com/repos/PlayCity-altV/{projectName}/compare/{lastPreviousTag}...{lastTag}";
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

    static async Task<string[]?> GetTags(string projectName)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            client.DefaultRequestHeaders.Add("User-Agent", "PlayCity");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Config.Entries.AuthToken);

            string apiUrl = $"https://api.github.com/repos/PlayCity-altV/{projectName}/tags?per_page=100";
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