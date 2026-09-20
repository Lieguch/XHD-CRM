using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;

namespace XHD.Core.Repository
{
    public class CRM_ContactRepository : BaseRepository<CRM_Contact>, ICRM_ContactRepository
    {
        public CRM_ContactRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(CRM_Contact model)
        {
            var result = await _fsql.Update<CRM_Contact>()
                .SetSource(model)
                .IgnoreColumns(a => new {  a.create_id, a.create_time })
                .ExecuteAffrowsAsync();

            return result;
        }


        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<CRM_Contact>> GridAsync(Expression<Func<CRM_Contact, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<CRM_Contact>()
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_Contact> result = new XHDData<CRM_Contact>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<CRM_Contact>> GridAsync(Expression<Func<CRM_Contact, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<CRM_Contact>()
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_Contact> result = new XHDData<CRM_Contact>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public async new Task<List<CRM_Contact>> GridAsync(Expression<Func<CRM_Contact, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Contact>()
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                    .Where(expWhere)
                    .ToListAsync(true);

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="OrderBy"></param>
        /// <returns></returns>
        public async new Task<List<CRM_Contact>> GridAsync(Expression<Func<CRM_Contact, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<CRM_Contact>()
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                    .Where(expWhere)
                    .OrderBy(OrderBy)
                    .ToListAsync(true);

            return data;
        }

        /// <summary>
        /// Sprint 4 Wave 3 #05：批量插入联系人。
        /// B 侧 CRM_Contact 无唯一键，逐条插入即可；插入失败（如 customer_id 外键约束冲突）记入 Error。
        /// 参数化执行（FreeSql Insert 表达式转参数），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="models">待插入的联系人列表</param>
        /// <returns>批量导入结果</returns>
        public async Task<ExcelImportResult> ImportRangeAsync(List<CRM_Contact> models)
        {
            var result = new ExcelImportResult();
            if (models == null || models.Count == 0)
            {
                result.Message = "导入数据为空";
                return result;
            }

            int rowNum = 0;
            foreach (var model in models)
            {
                rowNum++;
                if (model == null || string.IsNullOrWhiteSpace(model.C_name))
                {
                    result.Fail(rowNum, "联系人姓名不能为空");
                    continue;
                }

                var rows = await _fsql.Insert(model).ExecuteAffrowsAsync();
                if (rows > 0)
                {
                    result.Add();
                }
                else
                {
                    result.Fail(rowNum, $"联系人【{model.C_name}】插入失败");
                }
            }

            return result;
        }
    }
}
