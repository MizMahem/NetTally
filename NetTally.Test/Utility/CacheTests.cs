﻿using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetTally.Cache;
using NetTally.SystemInfo;

namespace TallyUnitTest.Utility
{
    [TestClass]
    public class CacheTests
    {
        static ICache<string> cache = GZStringCache.Instance;
        static string resourceContent = string.Empty;

        [ClassInitialize]
#if NETCOREAPP
        public static async Task Initialize(TestContext context)
#else
        public static async void Initialize(TestContext context)
#endif
        {
            resourceContent = await LoadResource.Read("Resources/RenascenceSV.html");

            cache = GZStringCache.Instance;
        }

        [TestInitialize]
        public void Prepare()
        {
            cache.Clear();
        }

        [TestMethod]
        public void ContentLoaded()
        {
            Assert.IsFalse(string.IsNullOrEmpty(resourceContent));
            Assert.IsTrue(resourceContent.Length > 200000);
            Assert.IsTrue(resourceContent.Length < 250000);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task Cache_null_key()
        {
            await cache.AddAsync(null, null, CacheInfo.DefaultExpiration);
        }

        [TestMethod]
        public async Task Cache_null_data()
        {
            string data = null;

            await cache.AddAsync("null data", data, CacheInfo.DefaultExpiration);
            var result = await cache.GetAsync("null data");

            Assert.IsTrue(result.found);
            Assert.AreEqual("", result.content);
        }

        [TestMethod]
        public async Task Cache_page_data()
        {
            await cache.AddAsync("page data", resourceContent, CacheInfo.DefaultExpiration);
            var result = await cache.GetAsync("page data");

            Assert.IsTrue(result.found);
            Assert.AreEqual(resourceContent, result.content);
        }

        [TestMethod]
        public async Task Cache_expired_data()
        {
            DateTime clockTime = new DateTime(2017, 7, 1, 12, 0, 0);
            DateTime expireTime = new DateTime(2017, 7, 1, 11, 59, 0);

            var clock = new StaticClock(clockTime);
            cache.SetClock(clock);

            await cache.AddAsync("page data", resourceContent, expireTime);
            var result = await cache.GetAsync("page data");

            Assert.IsFalse(result.found);
        }

        [TestMethod]
        public async Task Cache_unexpired_data()
        {
            DateTime clockTime = new DateTime(2017, 7, 1, 12, 0, 0);
            DateTime expireTime = new DateTime(2017, 7, 1, 12, 1, 0);

            var clock = new StaticClock(clockTime);
            cache.SetClock(clock);

            await cache.AddAsync("page data", resourceContent, expireTime);
            var result = await cache.GetAsync("page data");

            Assert.IsTrue(result.found);
            Assert.AreEqual(resourceContent, result.content);
        }

        [TestMethod]
        public async Task Cache_expired_data_invalidate()
        {
            DateTime clockTime = new DateTime(2017, 7, 1, 12, 0, 0);
            DateTime expireTime = new DateTime(2017, 7, 1, 11, 59, 0);

            var clock = new StaticClock(clockTime);
            cache.SetClock(clock);

            int storeCount = cache.MaxCacheEntries + 2;
            for (int i = 0; i < storeCount; i++)
            {
                await cache.AddAsync($"page data {i}", "A page of data", expireTime);
            }

            Assert.AreEqual(storeCount, cache.Count);
            cache.InvalidateCache();
            Assert.AreEqual(0, cache.Count);
        }
    }
}
