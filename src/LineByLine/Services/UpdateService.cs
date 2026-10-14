using System.Reflection;
using System.Text;
using Velopack;
using Velopack.Sources;

namespace LineByLine.Services;

/// <summary>
/// Updates from the project's GitHub Releases via Velopack. Only the installed app can update
/// itself; a build run from source reports <see cref="IsInstalled"/> = false.
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer";
    public const string ReleasesUrl = RepoUrl + "/releases";

    private readonly UpdateManager? _manager;

    /// <summary>An update that has been downloaded and will install on exit (or on restart).</summary>
    public UpdateInfo? Downloaded { get; private set; }

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
        }
        catch (Exception)
        {
            _manager = null; // not installed, or the install is damaged: updates simply unavailable
        }
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    /// <summary>Asks GitHub for a newer release. Null when already up to date.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (_manager == null || !IsInstalled)
            return null;
        return await _manager.CheckForUpdatesAsync();
    }

    public async Task DownloadAsync(UpdateInfo update, Action<int>? progress = null)
    {
        if (_manager == null)
            return;
        await _manager.DownloadUpdatesAsync(update, progress);
        Downloaded = update;
    }

    /// <summary>Installs a downloaded update and restarts the app. Does not return.</summary>
    public void ApplyAndRestart(UpdateInfo update) => _manager?.ApplyUpdatesAndRestart(update.TargetFullRelease);

    /// <summary>Installs a downloaded update quietly once the app is closed.</summary>
    public void ApplyOnExit(UpdateInfo update) =>
        _manager?.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);

    public static string VersionOf(UpdateInfo update) => update.TargetFullRelease.Version.ToString();

    /// <summary>The release's notes from GitHub as plain lines: headings unmarked, list items as bullets.</summary>
    public static string NotesOf(UpdateInfo update)
    {
        var markdown = update.TargetFullRelease.NotesMarkdown;
        if (string.IsNullOrWhiteSpace(markdown))
            return "No release notes were published with this version.";

        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('#'))
                line = line.TrimStart('#').Trim();
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                line = "•  " + line[2..];
            sb.AppendLine(line.Replace("**", "").Replace("`", ""));
        }
        return sb.ToString().TrimEnd();
    }
}
