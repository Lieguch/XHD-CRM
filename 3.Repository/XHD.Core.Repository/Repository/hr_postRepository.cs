using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    public class hr_postRepository : BaseRepository<hr_post>, Ihr_postRepository
    {
        public hr_postRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #34：更新单个岗位的 emp_id 与 default_post。
        /// </summary>
        public async Task<bool> UpdatePostEmpAsync(string postId, string empId, int? defaultPost)
        {
            if (string.IsNullOrWhiteSpace(postId))
            {
                return false;
            }

            int rows = await _fsql.Update<hr_post>()
                .Set(a => a.emp_id, empId ?? string.Empty)
                .Set(a => a.default_post, defaultPost)
                .Where(a => a.id == postId)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #35：清空员工所有岗位的 emp_id / default_post。
        /// </summary>
        public async Task<bool> UpdatePostEmpbyEidAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return false;
            }

            int rows = await _fsql.Update<hr_post>()
                .Set(a => a.emp_id, string.Empty)
                .Set(a => a.default_post, 0)
                .Where(a => a.emp_id == empId)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #88：按员工 id 取该员工名下所有岗位。
        /// </summary>
        public async Task<List<hr_post>> GetPostByEmpIdAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<hr_post>();
            }

            return await _fsql.Select<hr_post>()
                .Where(a => a.emp_id == empId)
                .OrderBy(a => a.default_post)
                .ToListAsync();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #89：按岗位名模糊搜索。
        /// </summary>
        public async Task<List<hr_post>> SerchAsync(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return new List<hr_post>();
            }

            string pattern = searchText;

            return await _fsql.Select<hr_post>()
                .Where(a => a.post_name.Contains(pattern))
                .ToListAsync();
        }
    }
}
