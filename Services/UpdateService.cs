using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace WindowScreenTime.Services;

public static class UpdateService
{
    public const string RepoOwner = "mafiamax";
    public const string RepoName = "WindowScreenTime";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    public const string RepoHtmlUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowScreenTime-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static string GetCurrentVersionString()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        if (ver == null) return "1.0.0";
        return $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
    }

    public static async Task CheckForUpdatesAsync(bool isManualCheck)
    {
        string currentVerStr = GetCurrentVersionString();
        Version currentVersion = Version.TryParse(currentVerStr, out var cv) ? cv : new Version(1, 0, 0);

        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (isManualCheck)
                {
                    System.Windows.MessageBox.Show(
                        $"Kullandığınız sürüm güncel (v{currentVerStr}).\nGitHub üzerinde henüz daha yeni bir sürüm yayınlanmamış.",
                        "WindowScreenTime - Güncelleme Kontrolü",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string cleanTag = tagName.TrimStart('v', 'V').Trim();
            string releaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? RepoHtmlUrl : RepoHtmlUrl;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";

                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(assetUrl))
                    {
                        downloadUrl = assetUrl;
                        if (assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }
            }

            if (Version.TryParse(cleanTag, out var remoteVersion) && remoteVersion > currentVersion)
            {
                string notesSummary = string.IsNullOrWhiteSpace(releaseNotes)
                    ? ""
                    : $"\n\nSürüm Notları:\n{(releaseNotes.Length > 300 ? releaseNotes[..300] + "..." : releaseNotes)}";

                var ask = System.Windows.MessageBox.Show(
                    $"Yeni bir WindowScreenTime sürümü mevcut!\n\nMevcut Sürüm: v{currentVerStr}\nYeni Sürüm: v{remoteVersion}{notesSummary}\n\nŞimdi indirip güncellemek ister misiniz?",
                    "WindowScreenTime - Yeni Güncelleme Mevcut",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (ask == MessageBoxResult.Yes)
                {
                    if (!string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        await DownloadAndRunInstallerAsync(downloadUrl);
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true });
                    }
                }
            }
            else if (isManualCheck)
            {
                System.Windows.MessageBox.Show(
                    $"WindowScreenTime güncel!\nMevcut sürümünüz: v{currentVerStr}",
                    "WindowScreenTime - Güncelleme Kontrolü",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (isManualCheck)
            {
                System.Windows.MessageBox.Show(
                    $"GitHub üzerinden güncelleme kontrolü yapılırken bağlantı hatası oluştu:\n{ex.Message}",
                    "WindowScreenTime - Güncelleme Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private static async Task DownloadAndRunInstallerAsync(string downloadUrl)
    {
        try
        {
            string tempSetupPath = Path.Combine(Path.GetTempPath(), $"WindowScreenTime_Setup_{Guid.NewGuid():N}.exe");
            byte[] data = await Http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempSetupPath, data);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempSetupPath,
                UseShellExecute = true
            });

            if (System.Windows.Application.Current is App app)
            {
                app.ExitApplication();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Güncelleme paketi indirilirken hata oluştu:\n{ex.Message}",
                "WindowScreenTime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
