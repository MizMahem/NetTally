﻿using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace NetTally
{
    public class WebPageProvider2 : IPageProvider
    {
        private enum StatusType
        {
            None,
            Requested,
            Retry,
            Error,
            Failed,
            Cancelled,
            Cached,
            Loaded
        }

        // Maximum number of simultaneous connections allowed, to guard against hammering the server.
        const int maxSimultaneousConnections = 4;
        readonly SemaphoreSlim ss = new SemaphoreSlim(maxSimultaneousConnections);

        HttpClientHandler webHandler;
        HttpClient client;

        WebCache Cache { get; } = WebCache.Instance;
        string UserAgent { get; set; }

        bool _disposed = false;

        public WebPageProvider2()
        {
            SetupUserAgent();
            SetupHandler();
            SetupClient();
        }

        #region Disposal
        ~WebPageProvider2()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true); //I am calling you from Dispose, it's safe
            GC.SuppressFinalize(this); //Hey, GC: don't bother calling finalize later
        }

        protected virtual void Dispose(bool itIsSafeToAlsoFreeManagedObjects)
        {
            if (_disposed)
                return;

            if (itIsSafeToAlsoFreeManagedObjects)
            {
                ss.Dispose();
                client?.Dispose();
                webHandler?.Dispose();
            }

            _disposed = true;
        }
        #endregion

        #region Event handlers
        public event EventHandler<MessageEventArgs> StatusChanged;

        /// <summary>
        /// Function to raise events when page load status has been updated.
        /// </summary>
        /// <param name="message">The message to send to any listeners.</param>
        protected void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, new MessageEventArgs(message));
        }
        #endregion

        #region Public interface functions
        /// <summary>
        /// Allow manual clearing of the page cache.
        /// </summary>
        public void ClearPageCache()
        {
            Cache.Clear();
        }

        /// <summary>
        /// If we're notified that a given attempt to load pages is done, we can
        /// tell the web page cache to expire old data.
        /// </summary>
        public void DoneLoading()
        {
            Cache.ExpireCache(DateTime.Now);
        }

        /// <summary>
        /// Load the specified thread page and return the document as an HtmlDocument.
        /// </summary>
        /// <param name="url">The thread URL.</param>
        /// <param name="pageNum">The page number in the thread to load.</param>
        /// <param name="caching">Whether to use or bypass the cache.</param>
        /// <param name="token">Cancellation token for the function.</param>
        /// <param name="shouldCache">Indicate whether the result of this page load should be cached.</param>
        /// <returns>An HtmlDocument for the specified page.</returns>
        public async Task<HtmlDocument> GetPage(string url, string shortDescription, Caching caching, CancellationToken token, bool shouldCache = true)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));


            if (caching == Caching.UseCache)
            {
                HtmlDocument page = Cache.Get(url);

                if (page != null)
                {
                    UpdateStatus(StatusType.Cached, shortDescription);
                    return page;
                }
            }

            UpdateStatus(StatusType.Requested, url);

            // Limit to no more than N parallel requests
            await ss.WaitAsync(token);

            try
            {
                string result = null;
                int maxtries = 5;
                int tries = 0;
                HttpResponseMessage response;

                try
                {
                    while (result == null && tries < maxtries && token.IsCancellationRequested == false)
                    {
                        if (tries > 0)
                        {
                            // If we have to retry loading the page, give it a short delay.
                            await Task.Delay(TimeSpan.FromSeconds(4));
                            UpdateStatus(StatusType.Retry, shortDescription);
                        }

                        using (response = await client.GetAsync(url, token).ConfigureAwait(false))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                result = await response.Content.ReadAsStringAsync();
                            }
                            else if (response.StatusCode == HttpStatusCode.NotFound)
                            {
                                tries = maxtries;
                            }
                            else
                            {
                                tries++;
                            }
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    UpdateStatus(StatusType.Error, shortDescription, e);
                    throw;
                }
                catch (OperationCanceledException e)
                {
                    Debug.WriteLine(string.Format("Operation was cancelled in task {0}.", Task.CurrentId.HasValue ? Task.CurrentId.Value : -1));
                    Debug.WriteLine($"Cancellation requested: {e.CancellationToken.IsCancellationRequested}  at source: {e.Source}");

                    if (token.IsCancellationRequested)
                        throw;
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                if (result == null)
                {
                    UpdateStatus(StatusType.Failed, shortDescription);
                    return null;
                }

                HtmlDocument htmldoc = new HtmlDocument();
                htmldoc.LoadHtml(result);

                if (shouldCache)
                    Cache.Add(url, result);

                UpdateStatus(StatusType.Loaded, shortDescription);

                return htmldoc;
            }
            finally
            {
                ss.Release();
            }
        }
        #endregion

        #region Private support functions
        /// <summary>
        /// Setup the user agent string to be used when making requests.
        /// </summary>
        private void SetupUserAgent()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var product = (AssemblyProductAttribute)assembly.GetCustomAttribute(typeof(AssemblyProductAttribute));
            var version = (AssemblyInformationalVersionAttribute)assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute));
            UserAgent = $"{product.Product} ({version.InformationalVersion})";
        }

        /// <summary>
        /// Set up the HTTP client handler object for use in managing underlying connections.
        /// </summary>
        private void SetupHandler()
        {
            webHandler = new HttpClientHandler();

            CookieCollection cookies = ForumCookies.GetAllCookies();
            webHandler.CookieContainer.Add(cookies);

            webHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            // Increase from 2 simultaneous connections to 4
            ServicePointManager.DefaultConnectionLimit = maxSimultaneousConnections;
        }

        /// <summary>
        /// Set up the client to be used for making requests.
        /// </summary>
        private void SetupClient()
        {
            client = new HttpClient(webHandler);

            client.MaxResponseContentBufferSize = 1000000;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Accept", "text/html");
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip,deflate");
            client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
        }



        /// <summary>
        /// Handle generating messages to send to OnStatusChanged handler.
        /// </summary>
        /// <param name="status">The type of status message being raised.</param>
        /// <param name="details">The details that get inserted into the status message.</param>
        /// <param name="e">The exception, for an error message.</param>
        private void UpdateStatus(StatusType status, string details = null, Exception e = null)
        {
            StringBuilder sb = new StringBuilder();

            if (status == StatusType.Cancelled)
            {
                sb.Append("Tally cancelled!");
            }
            else
            {
                if (details?.Length > 0)
                {
                    switch (status)
                    {
                        case StatusType.Requested:
                            sb.Append(details);
                            break;
                        case StatusType.Retry:
                            sb.Append("Retrying: ");
                            sb.Append(details);
                            break;
                        case StatusType.Error:
                            sb.Append(details);
                            sb.Append(" : ");
                            sb.Append(e?.Message);
                            break;
                        case StatusType.Failed:
                            sb.Append("Failed to load: ");
                            sb.Append(details);
                            break;
                        case StatusType.Cached:
                            sb.Append(details);
                            sb.Append(" loaded from memory!");
                            break;
                        case StatusType.Loaded:
                            sb.Append(details);
                            sb.Append(" loaded!");
                            break;
                    }
                }
            }

            if (sb.Length > 0)
            {
                sb.Append("\n");
                OnStatusChanged(sb.ToString());
            }
        }
        #endregion
    }
}