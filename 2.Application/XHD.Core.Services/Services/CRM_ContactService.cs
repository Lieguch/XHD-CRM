using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class CRM_ContactService : BaseService<CRM_Contact>, ICRM_ContactService
    {
        ICRM_ContactRepository _irepositoryBase;

        public CRM_ContactService(ICRM_ContactRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 4 Wave 3 #05：批量导入联系人（service 层薄封装，委托 Repository 执行）。
        /// </summary>
        /// <param name="models">待插入的联系人列表</param>
        /// <returns>批量导入结果</returns>
        public async Task<ExcelImportResult> ImportAsync(List<CRM_Contact> models)
        {
            return await _irepositoryBase.ImportRangeAsync(models);
        }
    }
}
