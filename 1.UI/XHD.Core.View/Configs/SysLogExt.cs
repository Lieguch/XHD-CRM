using System.Reflection;

namespace XHD.Core.View.Configs
{
    /// <summary>
    /// 日志扩展类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SysLogExt<T>
    {
        /// <summary>
        /// 对比两个实体，输出差异实体
        /// </summary>
        /// <param name="oldT"></param>
        /// <param name="newT"></param>
        /// <returns></returns>
        public string LogContent(T oldT, T newT)
        {
            PropertyInfo[] mPi = typeof(T).GetProperties();

            string ContentText = "";

            for (int i = 0; i < mPi.Length; i++)
            {
                PropertyInfo pi = mPi[i];

                // 根因修复：switch 匹配串已是 pi.Name.ToLower()，而 C# switch 对字符串常量执行
                // 「与 == 相同的大小写敏感序数比较」（官方文档实证），故原写的 Pascal 大小写标签
                // "Delete_time" / "isDelete" 永远匹配不到 "delete_time" / "isdelete"，属于死分支——
                // 后果是全站 25 处 LogContent 调用对含 isDelete 列的实体（hr_employee/hr_department/
                // hr_position/CRM_Customer/Sys_role/Sys_Param/Product…）每次修改都多记一条
                // 【isDelete】:【0】=>【】 的假 diff，污染审计日志。统一改成小写后跳过规则真正生效。
                switch (pi.Name.ToLower())
                {
                    case "id":
                    case "create_id":
                    case "create_time":
                    case "delete_time":
                    case "follow_time":
                    case "customer_id":
                    case "arrears_invoice":
                    case "arrears_money":
                    case "invoice_money":
                    case "receive_money":
                    case "discount_amount":
                    case "news_content":
                    case "lastfollow":
                    case "isdelete":
                    case "sn":
                        continue;
                }

                var oldobj = pi.GetValue(oldT, null);
                var newobj = pi.GetValue(newT, null);

                //防止object出现null，null赋空值
                if (oldobj == null)
                {
                    oldobj = "";
                }

                if (newobj == null)
                {
                    newobj = "";
                }

                //连表属性直接不比较
                if (oldobj.GetType().ToString().Contains("XHD.Core.Models"))
                {
                    continue;
                }

                //将属性全部改为string进行判断
                if (!oldobj.ToString().Equals(newobj.ToString()) )
                {
                    ContentText += $"【{pi.Name}】:【{oldobj}】=>【{newobj}】; \n";

                    //Common.NLogger.WriteLog("log_", $"【{pi.Name}】:【{oldobj}】-【{oldobj.GetType()}】=>【{newobj}】-【{newobj.GetType()}】;{oldT.ToString()}");
                }
            }

            return ContentText;
        }

        /// <summary>
        /// 获取实体值的文本
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public string getEntityText(T t)
        {
            PropertyInfo[] mPi = typeof(T).GetProperties();

            string ContentText = "";

            for (int i = 0; i < mPi.Length; i++)
            {
                PropertyInfo pi = mPi[i];

                var objectvalue = pi.GetValue(t, null);

                if (objectvalue == null)
                {
                    objectvalue = "";
                }

                //连表属性直接不比较
                if (objectvalue.GetType().ToString().Contains("XHD.Core.Models"))
                {
                    continue;
                }

                ContentText += $"【{pi.Name}】:【{ objectvalue }】; \n";
            }

            Common.NLogger.WriteLog("Delete_", $"[{t}] => \n{ContentText}");

            return ContentText;
        }

    }
}
