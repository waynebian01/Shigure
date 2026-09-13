using System.Net;
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
    internal const string LatestBrowserDownloadUrl =
        "https://github.com/waynebian01/Shigure/releases/latest/download/SpellIcons.shgpack";
    internal const string ReleasesPageUrl =
        "https://github.com/waynebian01/Shigure/releases";
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/waynebian01/Shigure/releases?per_page=20";
    private const string AssetName = "SpellIcons.shgpack";
    private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;

    public SpellIconPackageDownloadService()
    {
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Shigure", "1.0"));
    }

    public async Task<SpellIconDownloadResult> UpdateAsync(
        IProgress<SpellIconDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // UpdateAsync is invoked by MainForm. Preserve that UI context so the final hot swap
        // cannot dispose images while a DataGridView is painting them on the UI thread.
        var asset = await ResolveLatestAssetAsync(progress, cancellationToken);

        var localPath = SpellIconCatalog.PackagePath;
        if (File.Exists(localPath) && SpellIconCatalog.IsPackageAvailable)
        {
            if (asset.Sha256 is not null)
            {
                progress?.Report(new SpellIconDownloadProgress("正在比较本地与远端 SHA-256……"));
                var localHash = await ComputeSha256Async(localPath, cancellationToken);
                if (string.Equals(localHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new SpellIconDownloadResult(
                        Changed: false,
                        UpToDate: true,
                        asset.Size ?? new FileInfo(localPath).Length,
                        localHash);
                }
            }
            else if (asset.Size is { } remoteSize && new FileInfo(localPath).Length == remoteSize)
            {
                progress?.Report(new SpellIconDownloadProgress("正在比较本地与远端文件大小……"));
                var localHash = await ComputeSha256Async(localPath, cancellationToken);
                return new SpellIconDownloadResult(
                    Changed: false,
                    UpToDate: true,
                    remoteSize,
                    localHash);
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
            var (downloadedHash, downloadedSize) = await DownloadAsync(
                    asset,
                    temporaryPath,
                    progress,
                    cancellationToken);
            if (asset.Sha256 is not null
                && !string.Equals(downloadedHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
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
                downloadedSize,
                downloadedHash);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private async Task<ReleaseAsset> ResolveLatestAssetAsync(
        IProgress<SpellIconDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        progress?.Report(new SpellIconDownloadProgress("正在读取 GitHub 最新正式版本……"));
        if (await TryResolveAsync(
                () => GetAssetFromApiReleaseAsync(new Uri(LatestReleaseApiUrl), cancellationToken),
                errors,
                cancellationToken) is { } latest)
        {
            return latest;
        }

        progress?.Report(new SpellIconDownloadProgress("正在改用 GitHub 发布列表……"));
        if (await TryResolveAsync(
                () => GetAssetFromApiReleaseListAsync(cancellationToken),
                errors,
                cancellationToken) is { } listed)
        {
            return listed;
        }

        progress?.Report(new SpellIconDownloadProgress("正在改用 GitHub 发布页直链……"));
        if (await TryResolveAsync(
                () => GetAssetFromBrowserDownloadAsync(new Uri(LatestBrowserDownloadUrl), cancellationToken),
                errors,
                cancellationToken) is { } browserLatest)
        {
            return browserLatest;
        }

        var versionTag = CurrentReleaseTag;
        progress?.Report(new SpellIconDownloadProgress($"正在改用当前版本 {versionTag} 发布页……"));
        if (await TryResolveAsync(
                () => GetAssetFromBrowserDownloadAsync(GetCurrentVersionDownloadUri(), cancellationToken),
                errors,
                cancellationToken) is { } versioned)
        {
            return versioned;
        }

        throw new HttpRequestException(
            "无法从 GitHub 获取技能/物品数据包。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors));
    }

    private static async Task<ReleaseAsset?> TryResolveAsync(
        Func<Task<ReleaseAsset>> resolve,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resolve().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            errors.Add("GitHub 请求超时。");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException
            or FileNotFoundException or JsonException or IOException)
        {
            errors.Add(ex.Message);
            return null;
        }
    }

    private async Task<ReleaseAsset> GetAssetFromApiReleaseAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(uri, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!TryReadAssetFromRelease(document.RootElement, out var asset))
        {
            throw new FileNotFoundException($"GitHub 最新正式版本中找不到资产 {AssetName}。");
        }

        return asset;
    }

    private async Task<ReleaseAsset> GetAssetFromApiReleaseListAsync(CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(new Uri(ReleasesApiUrl), cancellationToken)
            .ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub 发布列表格式无效。");
        }

        ReleaseAsset? prereleaseAsset = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!TryReadAssetFromRelease(release, out var asset))
            {
                continue;
            }

            var isPrerelease = release.TryGetProperty("prerelease", out var pre)
                && pre.ValueKind == JsonValueKind.True;
            if (!isPrerelease)
            {
                return asset;
            }

            prereleaseAsset ??= asset;
        }

        return prereleaseAsset
            ?? throw new FileNotFoundException($"GitHub 发布列表中找不到资产 {AssetName}。");
    }

    private async Task<ReleaseAsset> GetAssetFromBrowserDownloadAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ApiRequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                var url = response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString();
                throw new HttpRequestException(
                    $"检查数据包失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}（{url}）");
            }

            if (response.IsSuccessStatusCode
                && response.Content.Headers.ContentLength is { } size
                && size > 0)
            {
                return new ReleaseAsset(uri, size, Sha256: null);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HEAD 超时：仍尝试正式下载该直链。
        }
        catch (IOException)
        {
            // 网络抖动：仍尝试正式下载该直链。
        }

        // 405/403 等：部分 CDN 不支持 HEAD，改由正式下载时读取 Content-Length。
        return new ReleaseAsset(uri, Size: null, Sha256: null);
    }

    private static bool TryReadAssetFromRelease(JsonElement release, out ReleaseAsset asset)
    {
        asset = null!;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
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
                continue;
            }

            if (!item.TryGetProperty("size", out var sizeElement)
                || !sizeElement.TryGetInt64(out var size)
                || size <= 0)
            {
                continue;
            }

            var downloadUrl = item.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? sha256 = null;
            if (item.TryGetProperty("digest", out var digestElement))
            {
                var digest = digestElement.GetString();
                const string prefix = "sha256:";
                if (!string.IsNullOrWhiteSpace(digest)
                    && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = digest[prefix.Length..].Trim();
                    if (value.Length == 64 && value.All(Uri.IsHexDigit))
                    {
                        sha256 = value.ToUpperInvariant();
                    }
                }
            }

            asset = new ReleaseAsset(uri, size, sha256);
            return true;
        }

        return false;
    }

    private async Task<(string Sha256, long Size)> DownloadAsync(
        ReleaseAsset asset,
        string destination,
        IProgress<SpellIconDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendDownloadAsync(HttpMethod.Get, asset.DownloadUrl, cancellationToken)
            .ConfigureAwait(false);

        long? expectedSize = asset.Size;
        if (response.Content.Headers.ContentLength is { } contentLength)
        {
            if (expectedSize is { } size && contentLength != size)
            {
                throw new InvalidDataException(
                    $"数据包大小与 GitHub 资产信息不一致：预计 {size}，实际 {contentLength}。");
            }

            expectedSize = contentLength;
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
            if (expectedSize is { } size && downloaded > size)
            {
                throw new InvalidDataException("下载的数据超过 GitHub 声明的资产大小。");
            }

            if (expectedSize is { } total and > 0)
            {
                var percentage = (int)Math.Min(100, downloaded * 100 / total);
                if (percentage != lastPercentage)
                {
                    lastPercentage = percentage;
                    progress?.Report(new SpellIconDownloadProgress(
                        $"正在下载：{FormatBytes(downloaded)} / {FormatBytes(total)}",
                        percentage));
                }
            }
            else
            {
                progress?.Report(new SpellIconDownloadProgress(
                    $"正在下载：{FormatBytes(downloaded)}"));
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (downloaded <= 0)
        {
            throw new InvalidDataException("数据包下载结果为空。");
        }

        if (expectedSize is { } expected && downloaded != expected)
        {
            throw new InvalidDataException(
                $"数据包下载不完整：预计 {expected} 字节，实际 {downloaded} 字节。");
        }

        return (Convert.ToHexString(hash.GetHashAndReset()), downloaded);
    }

    private async Task<HttpResponseMessage> SendApiAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ApiRequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token)
            .ConfigureAwait(false);
        EnsureSuccess(response, "读取 GitHub 版本信息");
        return response;
    }

    private async Task<HttpResponseMessage> SendDownloadAsync(
        HttpMethod method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, method == HttpMethod.Head ? "检查数据包" : "下载数据包");
        return response;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        using (response)
        {
            var url = response.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
            throw new HttpRequestException(
                $"{action}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}（{url}）");
        }
    }

    internal static string CurrentReleaseTag
    {
        get
        {
            var version = typeof(SpellIconPackageDownloadService).Assembly.GetName().Version;
            if (version is null || version.Build < 0 || version.Revision < 0)
            {
                return "1.2.1.25";
            }

            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }

    internal static Uri GetCurrentVersionDownloadUri() =>
        new($"https://github.com/waynebian01/Shigure/releases/download/{CurrentReleaseTag}/{AssetName}");

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

    private sealed record ReleaseAsset(Uri DownloadUrl, long? Size, string? Sha256);
}
