using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shigure;

internal sealed record SpellIconDownloadProgress(string Message, int? Percentage = null);

internal sealed record SpellIconDownloadResult(
    bool Changed,
    bool UpToDate,
    long Size,
    string Sha256);

internal sealed class SpellIconPackageDownloadService : IDisposable
{
    internal const string LatestReleaseApiUrl =
        "https://api.github.com/repos/waynebian01/Shigure/releases/latest";
    private const string AssetName = "SpellIcons.shgpack";

    private readonly HttpClient _httpClient;

    public SpellIconPackageDownloadService()
    {
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Shigure", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<SpellIconDownloadResult> UpdateAsync(
        IProgress<SpellIconDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SpellIconDownloadProgress("正在读取 GitHub 最新正式版本……"));
        // UpdateAsync is invoked by MainForm. Preserve that UI context so the final hot swap
        // cannot dispose images while a DataGridView is painting them on the UI thread.
        var asset = await GetLatestAssetAsync(cancellationToken);

        var localPath = SpellIconCatalog.PackagePath;
        if (File.Exists(localPath))
        {
            progress?.Report(new SpellIconDownloadProgress("正在比较本地与远端 SHA-256……"));
            var localHash = await ComputeSha256Async(localPath, cancellationToken);
            if (string.Equals(localHash, asset.Sha256, StringComparison.OrdinalIgnoreCase)
                && SpellIconCatalog.IsPackageAvailable)
            {
                return new SpellIconDownloadResult(
                    Changed: false,
                    UpToDate: true,
                    asset.Size,
                    asset.Sha256);
            }
        }

        var dataDirectory = Path.GetDirectoryName(localPath)
            ?? throw new InvalidOperationException("无法确定技能图标数据目录。");
        Directory.CreateDirectory(dataDirectory);
        var temporaryPath = Path.Combine(
            dataDirectory,
            $".{AssetName}.{Guid.NewGuid():N}.download");

        try
        {
            var downloadedHash = await DownloadAsync(
                    asset,
                    temporaryPath,
                    progress,
                    cancellationToken);
            if (!string.Equals(downloadedHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"数据包 SHA-256 校验失败：远端 {asset.Sha256}，下载结果 {downloadedHash}。");
            }

            progress?.Report(new SpellIconDownloadProgress("正在验证并安装数据包……", 100));
            SpellIconCatalog.ValidatePackage(temporaryPath);
            SpellIconCatalog.InstallPackage(temporaryPath);
            return new SpellIconDownloadResult(
                Changed: true,
                UpToDate: false,
                asset.Size,
                asset.Sha256);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private async Task<ReleaseAsset> GetLatestAssetAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
                LatestReleaseApiUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub 最新正式版本没有返回 assets 列表。");
        }

        foreach (var item in assets.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameElement)
                || !string.Equals(nameElement.GetString(), AssetName, StringComparison.Ordinal))
            {
                continue;
            }

            var state = item.TryGetProperty("state", out var stateElement)
                ? stateElement.GetString()
                : null;
            if (!string.Equals(state, "uploaded", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"GitHub 资产 {AssetName} 尚未上传完成。");
            }

            if (!item.TryGetProperty("size", out var sizeElement)
                || !sizeElement.TryGetInt64(out var size)
                || size <= 0)
            {
                throw new InvalidDataException($"GitHub 资产 {AssetName} 的大小无效。");
            }

            var downloadUrl = item.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"GitHub 资产 {AssetName} 的下载地址无效。");
            }

            var digest = item.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;
            const string prefix = "sha256:";
            if (string.IsNullOrWhiteSpace(digest)
                || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"GitHub 资产 {AssetName} 缺少 SHA-256 digest。");
            }

            var sha256 = digest[prefix.Length..].Trim();
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"GitHub 资产 {AssetName} 的 SHA-256 digest 无效。");
            }

            return new ReleaseAsset(uri, size, sha256.ToUpperInvariant());
        }

        throw new FileNotFoundException($"GitHub 最新正式版本中找不到资产 {AssetName}。");
    }

    private async Task<string> DownloadAsync(
        ReleaseAsset asset,
        string destination,
        IProgress<SpellIconDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
                asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength != asset.Size)
        {
            throw new InvalidDataException(
                $"数据包大小与 GitHub 资产信息不一致：预计 {asset.Size}，实际 {contentLength}。");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1024 * 1024];
        long downloaded = 0;
        var lastPercentage = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            downloaded += read;
            if (downloaded > asset.Size)
            {
                throw new InvalidDataException("下载的数据超过 GitHub 声明的资产大小。");
            }

            var percentage = (int)Math.Min(100, downloaded * 100 / asset.Size);
            if (percentage != lastPercentage)
            {
                lastPercentage = percentage;
                progress?.Report(new SpellIconDownloadProgress(
                    $"正在下载：{FormatBytes(downloaded)} / {FormatBytes(asset.Size)}",
                    percentage));
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (downloaded != asset.Size)
        {
            throw new InvalidDataException(
                $"数据包下载不完整：预计 {asset.Size} 字节，实际 {downloaded} 字节。");
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest);
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 下次运行不会读取 .download 文件。
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record ReleaseAsset(Uri DownloadUrl, long Size, string Sha256);
}
