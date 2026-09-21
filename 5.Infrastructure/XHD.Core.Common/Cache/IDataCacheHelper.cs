using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.Common.Cache
{
    /// <summary>
    /// 内存缓存抽象接口（Sprint 9 新增，#158 DataCache.GetDataCache）。
    /// A 侧 <c>Common/DataCache.cs</c> 使用 <c>HttpRuntime.Cache</c>（ASP.NET WebForms 独有）；
    /// B 侧改用 <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>。
    /// 语义等价：GetCache(key) → cache.Get(key)；SetCache(key, value, exp) → cache.Set(key, value, exp)。
    /// </summary>
    public interface IDataCacheHelper
    {
        /// <summary>
        /// 获取缓存值（同步，与 A 侧 DataCache.GetCache 一致）。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>缓存对象；不存在或 key 为空返回 null</returns>
        object GetCache(string key);

        /// <summary>
        /// 获取缓存值（异步，等价于 GetCache 但返回 Task）。
        /// </summary>
        Task<object> GetCacheAsync(string key);

        /// <summary>
        /// 设置缓存（同步，与 A 侧 DataCache.SetCache 一致，无过期时间）。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">缓存值</param>
        void SetCache(string key, object value);

        /// <summary>
        /// 设置缓存（异步，带过期时间）。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">缓存值</param>
        /// <param name="absoluteExpiration">绝对过期时间（UTC），可为 null</param>
        /// <param name="slidingExpiration">滑动过期时长，可为 null</param>
        Task SetCacheAsync(string key, object value, DateTime? absoluteExpiration = null, TimeSpan? slidingExpiration = null);

        /// <summary>
        /// 移除缓存项（A 侧未提供，B 侧扩展，便于运维）。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>true 表示存在并已移除</returns>
        bool RemoveCache(string key);

        /// <summary>
        /// 查询当前缓存中的所有键（诊断用，生产慎用）。
        /// </summary>
        /// <returns>缓存键集合</returns>
        IReadOnlyCollection<string> GetAllKeys();
    }
}
