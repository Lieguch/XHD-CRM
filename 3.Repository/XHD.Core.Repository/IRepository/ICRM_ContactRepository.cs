using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;


namespace XHD.Core.IRepository
{
    public interface ICRM_ContactRepository: IXHDBaseRepository<CRM_Contact>
    {
        /// <summary>
        /// Sprint 4 Wave 3 #05：批量插入联系人。
        /// 逐条插入（B 侧 Contact 无唯一键约束，不做去重；如需去重请在业务层控制）。
        /// 已插入的行会成功；插入失败会记入 result.Error。
        /// </summary>
        /// <param name="models">待插入的联系人列表</param>
        /// <returns>批量导入结果</returns>
        Task<ExcelImportResult> ImportRangeAsync(List<CRM_Contact> models);
    }
}
