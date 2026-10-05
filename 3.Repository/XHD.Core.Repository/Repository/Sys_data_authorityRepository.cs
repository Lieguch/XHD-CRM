using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    /// <summary>
    /// 数据权限-指定部门 仓储实现（缺口 E）
    /// </summary>
    public class Sys_data_authorityRepository : BaseRepository<Sys_data_authority>, ISys_data_authorityRepository
    {
        public Sys_data_authorityRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 取角色已勾选的部门 id 列表。
        /// </summary>
        public async Task<List<string>> GetDepIdsByRoleIdAsync(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return new List<string>();
            }

            return await _fsql.Select<Sys_data_authority>()
                .Where(a => a.Role_id == roleId)
                .ToListAsync(a => a.dep_id);
        }

        /// <summary>
        /// 保存角色的指定部门勾选：先删后插（单一事务）。
        /// 参数化，禁止字符串拼接；depIds 去空去重。
        /// </summary>
        public async Task<int> SaveAsync(string roleId, IEnumerable<string> depIds, string createId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return 0;
            }

            var validDepIds = (depIds ?? Enumerable.Empty<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .Distinct()
                .ToList();

            using (var uow = _fsql.CreateUnitOfWork())
            {
                // 1. 删除该角色既有勾选（A 版 save 的 Delete 分支）
                await uow.Orm.Delete<Sys_data_authority>()
                    .Where(a => a.Role_id == roleId)
                    .ExecuteAffrowsAsync();

                var inserted = 0;

                // 2. 逐行插入（A 版 save 按逗号分割 depids 逐行 Add）
                if (validDepIds.Count > 0)
                {
                    var now = DateTime.Now;
                    var rows = validDepIds.Select(depId => new Sys_data_authority
                    {
                        id = Guid.NewGuid().ToString(),
                        Role_id = roleId,
                        dep_id = depId,
                        create_id = createId ?? string.Empty,
                        create_time = now
                    }).ToList();

                    inserted = await uow.Orm.Insert(rows).ExecuteAffrowsAsync();
                }

                uow.Commit();
                return inserted;
            }
        }
    }
}
