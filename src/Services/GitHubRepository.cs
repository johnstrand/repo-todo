using System.Text.RegularExpressions;

namespace RepoTodo.Services;

internal sealed record GitHubRepository(string Owner, string Name)
{
    private static readonly Regex RemotePattern = new(
        @"^(?:(?:https?|ssh)://(?:git@)?github\.com[:/]|git@github\.com[:/])(?<owner>[^/]+)/(?<name>[^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static GitHubRepository? FromRemoteUrls(IEnumerable<(string Name, string Url)> remotes)
    {
        var remoteList = remotes.ToList();
        var candidates = remoteList
            .OrderBy(remote => string.Equals(remote.Name, "origin", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(remote => remote.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url));

        foreach (var remoteUrl in candidates)
        {
            var match = RemotePattern.Match(remoteUrl!);
            if (match.Success)
            {
                return new GitHubRepository(match.Groups["owner"].Value, match.Groups["name"].Value);
            }
        }

        return null;
    }
}
