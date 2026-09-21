using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace XHD.Core.Common.Cache
{
    /// <summary>
    /// 内存缓存默认实现（Sprint 9 新增，#158）。
    /// 使用 <see cref="IMemoryCache"/>；GetAllKeys 通过 ConcurrentDictionary 影子表跟踪，
    /// 因 IMemoryCache 本身不暴露 Keys 集合。
    /// </summary>
    public class DataCacheHelper : IDataCacheHelper
    {
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, byte> _keys;
        private readonly ILogger<DataCacheHelper> _logger;

        public DataCacheHelper(IMemoryCache cache, ILogger<DataCacheHelper> logger = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keys = new ConcurrentDictionary<string, byte>();
            _logger = logger;
        }

        public object GetCache(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            if (_cache.TryGetValue(key, out object value))
            {
                return value;
            }
            return null;
        }

        public Task<object> GetCacheAsync(string key)
        {
            return Task.FromResult(GetCache(key));
        }

        public void SetCache(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger?.LogWarning("DataCacheHelper.SetCache: key 为空，忽略");
                return;
            }
            _cache.Set(key, value);
            _keys[key] = 0;
        }

        public Task SetCacheAsync(string key, object value, DateTime? absoluteExpiration = null, TimeSpan? slidingExpiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Task.CompletedTask;
            }
            var options = new MemoryCacheEntryOptions();
            if (absoluteExpiration.HasValue)
            {
                options.AbsoluteExpiration = absoluteExpiration;
            }
            if (slidingExpiration.HasValue)
            {
                options.SlidingExpiration = slidingExpiration;
            }
            if (absoluteExpiration == null && slidingExpiration == null)
            {
                // 无过期时间时依赖默认策略；此处显式设一个足够长的滑动过期以避免无限驻留
                options.SlidingExpiration = TimeSpan.FromHours(24);
            }
            _cache.Set(key, value, options);
            _keys[key] = 0;
            return Task.CompletedTask;
        }

        public bool RemoveCache(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }
            _cache.Remove(key);
            return _keys.TryRemove(key, out _);
        }

        public IReadOnlyCollection<string> GetAllKeys()
        {
            // 惰性清理：影子表可能包含已过期的键，扫描一次并剔除
            foreach (string key in _keys.Keys)
            {
                if (!_cache.TryGetValue(key, out _))
                {
                    _keys.TryRemove(key, out _);
                }
            }
            return _keys.Keys.ToArray();
        }
    }
}
