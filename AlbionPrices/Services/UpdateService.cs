using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AlbionPrices.Services;

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly Version _currentVersion;

    public string? LatestVersion { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public string? DownloadUrl { get; private set; }
    public bool IsUpdateAvailable { get; private set; }

    public event EventHandler<string>? UpdateDownloaded;
    public event EventHandler<string>? UpdateError;

    public UpdateService(string repoOwner, string repoName)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");

        _repoOwner = repoOwner;
        _repoName = repoName;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        _currentVersion = version ?? new Version(1, 0, 0);
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);

            var release = JsonSerializer.Deserialize<GitHubRelease>(response);
            if (release == null) return;

            LatestVersion = release.TagName?.TrimStart('v');
            ReleaseNotes = release.Body;

            if (string.IsNullOrEmpty(LatestVersion)) return;

            if (Version.TryParse(LatestVersion, out var latestVer))
            {
                IsUpdateAvailable = latestVer > _currentVersion;

                if (IsUpdateAvailable && release.Assets?.Count > 0)
                {
                    var asset = release.Assets.FirstOrDefault(a =>
                        a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
                    DownloadUrl = asset?.BrowserDownloadUrl;
                }
            }

            System.Diagnostics.Debug.WriteLine($"Update check: current={_currentVersion}, latest={LatestVersion}, available={IsUpdateAvailable}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    public async Task DownloadAndInstallUpdateAsync()
    {
        if (!IsUpdateAvailable || string.IsNullOrEmpty(DownloadUrl))
        {
            UpdateError?.Invoke(this, "No update available or download URL not found");
            return;
        }

        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe))
            {
                UpdateError?.Invoke(this, "Could not find current executable path");
                return;
            }

            var exeDir = Path.GetDirectoryName(currentExe)!;
            var tempPath = Path.Combine(Path.GetTempPath(), "AlbionPrices_Update");
            var extractPath = Path.Combine(tempPath, "files");

            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
            Directory.CreateDirectory(extractPath);

            System.Diagnostics.Debug.WriteLine($"Downloading update from: {DownloadUrl}");
            var zipPath = Path.Combine(tempPath, "update.zip");
            var bytes = await _httpClient.GetByteArrayAsync(DownloadUrl);
            await File.WriteAllBytesAsync(zipPath, bytes);
            System.Diagnostics.Debug.WriteLine($"Downloaded {bytes.Length} bytes");

            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var extractedExe = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("AlbionPrices", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(extractedExe))
                extractedExe = Path.Combine(extractPath, "AlbionPrices.exe");

            var batchPath = Path.Combine(tempPath, "update.bat");
            var batchContent = $@"@echo off
chcp 65001 >nul
echo Instalando actualizacion...
timeout /t 1 /nobreak >nul
xcopy /y /e /q ""{extractPath}\*"" ""{exeDir}""
if exist ""{Path.Combine(extractPath, "tessdata")}"" (
    xcopy /y /e /q ""{extractPath}\tessdata\*"" ""{Path.Combine(exeDir, "tessdata")}""
)
echo Iniciando nueva version...
start """" ""{extractedExe}""
del ""{zipPath}""
rmdir /s /q ""{tempPath}""
del ""%~f0""
";

            await File.WriteAllTextAsync(batchPath, batchContent);

            System.Diagnostics.Debug.WriteLine("Update prepared, launching installer script");

            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            UpdateDownloaded?.Invoke(this, LatestVersion ?? "");
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update download/install failed: {ex.Message}");
            UpdateError?.Invoke(this, ex.Message);
        }
    }

    private class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? Body { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}