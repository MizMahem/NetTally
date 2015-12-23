﻿using System;
using System.Threading.Tasks;
using System.Threading;
using HtmlAgilityPack;

namespace NetTally
{
    public interface IPageProvider : IDisposable
    {
        /// <summary>
        /// Asynchronously load a specific page.
        /// </summary>
        /// <param name="url">The URL of the page to load.  Cannot be null.</param>
        /// <param name="shortDescrip">A short description that can be used in status updates.  If null, no update will be given.</param>
        /// <param name="caching">Indicator of whether to query the cache for the requested page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="shouldCache">Indicates whether the result of this page load should be cached.</param>
        /// <returns>Returns an HTML document, if it can be loaded.</returns>
        /// <exception cref="ArgumentNullException">If url is null or empty.</exception>
        Task<HtmlDocument> GetPage(string url, string shortDescrip, Caching caching, CancellationToken token, bool shouldCache = true);

        /// <summary>
        /// Have an event that can be watched for status messages.
        /// </summary>
        event EventHandler<MessageEventArgs> StatusChanged;

        /// <summary>
        /// Clear the cache of any previously loaded pages.
        /// </summary>
        void ClearPageCache();

        /// <summary>
        /// If we're notified that a given attempt to load pages is done, we can
        /// tell the web page cache to expire old data.
        /// </summary>
        void DoneLoading();
    }
}
