using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;


namespace XHD.Core.IRepository
{
    public interface Ihr_postRepository : IXHDBaseRepository<hr_post>
    {
        /// <summary>
        /// Sprint 5 Wave 1 #34：更新单个岗位的 emp_id 和 default_post。
        /// 对应 A 侧 DAL.hr_post.UpdatePostEmp。
        /// </summary>
        /// <param name="postId">目标岗位 id</param>
        /// <param name="empId">新绑定的员工 id（空字符串表示清空）</param>
        /// <param name="defaultPost">是否此员工的默认岗位（0 / 1）</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        Task<bool> UpdatePostEmpAsync(string postId, string empId, int? defaultPost);

        /// <summary>
        /// Sprint 5 Wave 1 #35：清空员工所有岗位分配。
        /// 对应 A 侧 DAL.hr_post.UpdatePostEmpbyEid：SET emp_id='', default_post=0 WHERE emp_id=@emp_id。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>是否至少清空一行（Rows &gt; 0）</returns>
        Task<bool> UpdatePostEmpbyEidAsync(string empId);

        /// <summary>
        /// Sprint 5 Wave 1 #88：按员工 id 查岗位列表。
        /// 对应 A 侧 Server.hr_post.getpostbyempid。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>岗位列表</returns>
        Task<List<hr_post>> GetPostByEmpIdAsync(string empId);

        /// <summary>
        /// Sprint 5 Wave 1 #89：按岗位名模糊搜索。
        /// 对应 A 侧 Server.hr_post.serch：post_name LIKE N'%{text}%'。
        /// </summary>
        /// <param name="searchText">搜索关键词</param>
        /// <returns>岗位列表</returns>
        Task<List<hr_post>> SerchAsync(string searchText);
    }
}
