using Lucene.Net.Util;
using ObjectSearch;
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlConverter;

namespace Conduit.ViewModel
{
    public class ToolsViewModel : ObservableCollection<ToolViewModel>
    {
        private readonly ObjectSearchEngine _toolSearch;

        private static readonly Uri ToolsFolderApiUri = new(
            "https://api.github.com/repos/tomlm/Conduit/contents/src/Conduit/Tools?ref=main");

        private static readonly string ToolsCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Conduit",
            "Tools");

        public ToolsViewModel()
        {
            _toolSearch = new ObjectSearchEngine();

            // add listener callback for items added to this collection
            this.CollectionChanged += (s, e) =>
            {
                lock (_toolSearch)
                {
                    if (e.NewItems != null)
                    {
                        _toolSearch.AddObjects(e.NewItems.Cast<ToolViewModel>());
                    }
                    if (e.OldItems != null)
                    {
                        _toolSearch.RemoveObjects(e.OldItems.Cast<ToolViewModel>());
                    }
                }
            };


            Directory.CreateDirectory(ToolsCacheDirectory);

            // Populate synchronously from cache first (fast UI start)
            AddToolsFromDirectory(ToolsCacheDirectory);

            // Then refresh from GitHub in the background
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
            catch (Exception)
            {
                return Array.Empty<ToolViewModel>();
            }
        }

        private void AddToolsFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            this.AddRange(Directory.GetFiles(directory, "*.yml")
                .Select(TryLoadToolFromFile)
                .Where(tool => tool != null)!
                .Cast<ToolViewModel>());
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
                Console.WriteLine($"Error loading tool definition from {file}: {ex.Message}");
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
                        Console.WriteLine("GitHub API rate limit exceeded while listing tools.");
                    }
                    else
                    {
                        Console.WriteLine($"GitHub tools list failed: {(int)listResponse.StatusCode} {listResponse.ReasonPhrase}");
                    }
                    return;
                }

                await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var items = await JsonSerializer.DeserializeAsync<List<GitHubContentItem>>(listStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (items == null)
                {
                    return;
                }

                var yamlItems = items
                    .Where(i => i.DownloadUrl.LocalPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || i.DownloadUrl.LocalPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var item in yamlItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var localPath = Path.Combine(ToolsCacheDirectory, Path.GetFileName(item.DownloadUrl.LocalPath));

                    // Avoid re-downloading if unchanged (best-effort via ETag)
                    var request = new HttpRequestMessage(HttpMethod.Get, item.DownloadUrl);
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
                            Console.WriteLine("GitHub API rate limit exceeded while downloading tools.");
                            return;
                        }

                        Console.WriteLine($"GitHub download failed for {item.Name}: {(int)fileResponse.StatusCode} {fileResponse.ReasonPhrase}");
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

                    // Replace existing with same id, else add.
                    var existing = this.FirstOrDefault(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        var index = this.IndexOf(existing);
                        if (index >= 0)
                        {
                            this[index] = tool;
                        }
                    }
                    else
                    {
                        this.Add(tool);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing tools from GitHub: {ex.Message}");
            }
        }

        private static HttpClient CreateGitHubHttpClient()
        {
            var http = new HttpClient();

            // GitHub API requires a User-Agent. Accept raw content for download URLs.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Conduit/1.0");
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
