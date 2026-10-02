using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>
    /// Fetches an app's screenshot once and keeps it on disk.
    /// </summary>
    /// <remarks>
    /// Fetched only when someone opens an app's details, never with the catalog: a hundred
    /// pictures would turn a few-kilobyte catalog refresh into a download measured in tens of
    /// megabytes, for pictures most people never look at.
    ///
    /// Cached by a hash of the URL. Registrations point at pinned files (a commit, a release, an
    /// upload), so a URL's content does not change and nothing here ever revalidates. A
    /// registration that wants a new picture changes its URL.
    /// </remarks>
    public static class ScreenshotCache
    {
        private const string LogCategory = "Screenshots";

        /// <summary>
        /// The largest picture worth fetching. An animated GIF of a whole session can run to
        /// tens of megabytes, and the console shows its first frame in a few dozen cells.
        /// </summary>
        public const long MaxBytes = 16 * 1024 * 1024;

        private static readonly string CacheDirectory = Path.Combine(Log.StateDirectory, "Screenshots");

        private static readonly Lazy<HttpClient> Http = new(() =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Conch", "1.0"));
            return http;
        });

        /// <summary>Where <paramref name="url"/> is kept once fetched.</summary>
        public static string CachePathFor(string url, string directory)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
            return Path.Combine(directory, hash[..32]);
        }

        /// <summary>
        /// The picture's bytes, from the cache or the network; null when it cannot be had.
        /// </summary>
        public static async Task<byte[]?> GetAsync(string url, CancellationToken cancellationToken)
        {
            var path = CachePathFor(url, CacheDirectory);

            try
            {
                if (File.Exists(path))
                {
                    return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                }

                using var response = await Http.Value
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning(LogCategory, $"{url}: {(int)response.StatusCode} {response.ReasonPhrase}");
                    return null;
                }

                if (response.Content.Headers.ContentLength > MaxBytes)
                {
                    Log.Warning(LogCategory, $"{url}: {response.Content.Headers.ContentLength} bytes is more than {MaxBytes}; not fetched.");
                    return null;
                }

                var bytes = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
                if (bytes == null)
                {
                    Log.Warning(LogCategory, $"{url}: larger than {MaxBytes} bytes; abandoned.");
                    return null;
                }

                // Written beside and moved into place, so a half-written file is never read as a
                // picture by the next lookup.
                Directory.CreateDirectory(CacheDirectory);
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temp, path, overwrite: true);

                return bytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"{url}: {ex.Message}");
                return null;
            }
        }

        // Content-Length is optional, so the cap is enforced on what actually arrives too.
        private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                {
                    return null;
                }
            }

            return buffer.ToArray();
        }
    }
}
