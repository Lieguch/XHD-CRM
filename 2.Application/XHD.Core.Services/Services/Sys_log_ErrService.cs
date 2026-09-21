
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Services
{
    /// <summary>
    /// 系统错误日志服务实现
    /// Sprint 8 #41 GetLogtypeAsync 直接调仓储方法
    /// </summary>
    public class Sys_log_ErrService : BaseService<Sys_log_Err>, ISys_log_ErrService
    {
        private readonly ISys_log_ErrRepository _repo;

        public Sys_log_ErrService(ISys_log_ErrRepository repository)
        {
            _irepository = repository;
            _repo = repository;
        }

        /// <summary>
        /// 获取错误日志类型字典。
        /// </summary>
        /// <returns>错误类型列表</returns>
        public async Task<List<(int typeid, string type)>> GetLogtypeAsync()
        {
            return await _repo.GetLogtypeAsync();
        }
    }
}
