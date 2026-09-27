using Avalonia.Threading;
using Conch.Utilities;
using ObjectSearch;
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlConverter;

namespace Conch.ViewModel
{
    /// <summary>
    /// The catalog of registered applications, assembled from three layers (lowest priority
    /// first): the catalog shipped alongside the executable, the per-user cache, and the
    /// remote feed.
    /// </summary>
    public class ToolsViewModel : ObservableCollection<ToolViewModel>
    {
        private const string LogCategory = "Catalog";

        private readonly ObjectSearchEngine _toolSearch;

        private static readonly Uri ToolsFolderApiUri = new(
            "https://api.github.com/repos/tomlm/Conch/contents/src/Conch/Tools?ref=main");

        /// <summary>
        /// Catalog shipped with the build. Guarantees a usable app list on a machine that has
        /// never had network access, and on first run before the remote feed has been read.
        /// </summary>
        private static readonly string SeedToolsDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Tools");

        private static readonly string ToolsCacheDirectory = Path.Combine(
            Log.StateDirectory,
            "Tools");

        public ToolsViewModel()
        {
            _toolSearch = new ObjectSearchEngine();

            // add listener callback for items added to this collection
            this.CollectionChanged += (s, e) =>
            {
                lock (_toolSearch)
                {
                    if (e.OldItems != null)
                    {
                        _toolSearch.RemoveObjects(e.OldItems.Cast<ToolViewModel>());
                    }
                    if (e.NewItems != null)
                    {
                        _toolSearch.AddObjects(e.NewItems.Cast<ToolViewModel>());
                    }
                }
            };

            Directory.CreateDirectory(ToolsCacheDirectory);

            // Populate synchronously so the UI starts with a catalog: shipped definitions first,
            // then the cache on top so a previously refreshed definition wins over the one we shipped.
            AddToolsFromDirectory(SeedToolsDirectory);
            AddToolsFromDirectory(ToolsCacheDirectory);

            Log.Info(LogCategory, $"Loaded {Count} tool definition(s) from disk.");

            // Then refresh from the remote feed in the background
            _ = RefreshFromGitHubAsync();
        }

        public IEnumerable<ToolViewModel> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return this;
            }
            try
            {
                return _toolSearch.Search<ToolViewModel>(query).Select(sr => sr.Value!).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, $"Search for {query} failed", ex);
                return Array.Empty<ToolViewModel>();
            }
        }

        /// <summary>
        /// Adds a tool, replacing any existing entry that has the same id.
        /// </summary>
        private void Upsert(ToolViewModel tool)
        {
            var existing = this.FirstOrDefault(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Add(tool);
                return;
            }

            var index = IndexOf(existing);
            if (index >= 0)
            {
                this[index] = tool;
            }
        }

        private void AddToolsFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Log.Info(LogCategory, $"No tool definitions at {directory}.");
                return;
            }

            foreach (var file in Directory.GetFiles(directory, "*.yml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var tool = TryLoadToolFromFile(file);
                if (tool != null)
                {
                    Upsert(tool);
                }
            }
        }

        private static ToolViewModel? TryLoadToolFromFile(string file)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var tool = YamlConvert.DeserializeObject<ToolViewModel>(yaml);
                tool.Validate();
                return tool;
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, $"Error loading tool definition from {file}", ex);
                return null;
            }
        }

        private async Task RefreshFromGitHubAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var http = CreateGitHubHttpClient();

                using var listResponse = await http.GetAsync(ToolsFolderApiUri, cancellationToken).ConfigureAwait(false);
                if (!listResponse.IsSuccessStatusCode)
                {
                    if ((int)listResponse.StatusCode == 403 && listResponse.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
                    {
                        Log.Warning(LogCategory, "GitHub API rate limit exceeded while listing tools; keeping the local catalog.");
                    }
                    else
                    {
                        Log.Warning(LogCategory, $"Tools list failed: {(int)listResponse.StatusCode} {listResponse.ReasonPhrase} ({ToolsFolderApiUri}); keeping the local catalog.");
                    }
                    return;
                }

                await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var items = await JsonSerializer.DeserializeAsync<List<GitHubContentItem>>(listStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (items == null)
                {
                    Log.Warning(LogCategory, "Tools list returned no content.");
                    return;
                }

                var yamlItems = items
                    .Where(i => i.DownloadUrl != null &&
                        (i.DownloadUrl.LocalPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                         i.DownloadUrl.LocalPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var updated = 0;

                foreach (var item in yamlItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var downloadUrl = item.DownloadUrl!;
                    var localPath = Path.Combine(ToolsCacheDirectory, Path.GetFileName(downloadUrl.LocalPath));

                    // Avoid re-downloading if unchanged (best-effort via ETag)
                    var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    if (File.Exists(localPath))
                    {
                        var etagPath = localPath + ".etag";
                        if (File.Exists(etagPath))
                        {
                            var etag = await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag.Trim(), out var parsed))
                            {
                                request.Headers.IfNoneMatch.Add(parsed);
                            }
                        }
                    }

                    using var fileResponse = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (fileResponse.StatusCode == System.Net.HttpStatusCode.NotModified)
                    {
                        continue;
                    }

                    if (!fileResponse.IsSuccessStatusCode)
                    {
                        if ((int)fileResponse.StatusCode == 403 && fileResponse.Headers.TryGetValues("X-RateLimit-Remaining", out var dlRemaining) && dlRemaining.FirstOrDefault() == "0")
                        {
                            Log.Warning(LogCategory, "GitHub API rate limit exceeded while downloading tools.");
                            return;
                        }

                        Log.Warning(LogCategory, $"Download failed for {item.Name}: {(int)fileResponse.StatusCode} {fileResponse.ReasonPhrase}");
                        continue;
                    }

                    var newEtag = fileResponse.Headers.ETag?.ToString();
                    var yaml = await fileResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    await File.WriteAllTextAsync(localPath, yaml, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(newEtag))
                    {
                        await File.WriteAllTextAsync(localPath + ".etag", newEtag, cancellationToken).ConfigureAwait(false);
                    }

                    var tool = TryLoadToolFromFile(localPath);
                    if (tool == null)
                    {
                        continue;
                    }

                    // This collection is bound to the UI, so mutate it only on the UI thread.
                    await Dispatcher.UIThread.InvokeAsync(() => Upsert(tool));
                    updated++;
                }

                Log.Info(LogCategory, updated == 0
                    ? "Catalog is up to date."
                    : $"Refreshed {updated} tool definition(s) from the remote catalog.");
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, "Error refreshing tools from the remote catalog", ex);
            }
        }

        private static HttpClient CreateGitHubHttpClient()
        {
            var http = new HttpClient();

            // GitHub API requires a User-Agent. Accept raw content for download URLs.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Conch/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            return http;
        }

        private sealed class GitHubContentItem
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("download_url")]
            public Uri? DownloadUrl { get; set; }
        }
    }
}
