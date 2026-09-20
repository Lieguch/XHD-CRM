using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface ICRM_ContactService:IBaseService<CRM_Contact>
    {
        /// <summary>
        /// Sprint 4 Wave 3 #05：批量导入联系人（service 层薄封装）。
        /// </summary>
        /// <param name="models">待插入的联系人列表（已构建完成）</param>
        /// <returns>批量导入结果</returns>
        Task<ExcelImportResult> ImportAsync(List<CRM_Contact> models);
    }
}
