using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface Ihr_postService : IBaseService<hr_post>
    {
        /// <summary>
        /// Sprint 5 Wave 1 #34：更新单个岗位的 emp_id 与 default_post。
        /// </summary>
        Task<bool> UpdatePostEmpAsync(string postId, string empId, int? defaultPost);

        /// <summary>
        /// Sprint 5 Wave 1 #35：清空员工所有岗位的 emp_id 与 default_post。
        /// </summary>
        Task<bool> UpdatePostEmpbyEidAsync(string empId);

        /// <summary>
        /// Sprint 5 Wave 1 #88：按员工 id 取岗位列表。
        /// </summary>
        Task<List<hr_post>> GetPostByEmpIdAsync(string empId);

        /// <summary>
        /// Sprint 5 Wave 1 #89：按岗位名模糊搜索。
        /// </summary>
        Task<List<hr_post>> SerchAsync(string searchText);
    }
}
