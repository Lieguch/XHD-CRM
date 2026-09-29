using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;

namespace XHD.Core.Common
{
    public class XHDResult
    {
        private XHDResult() { }

        /// <summary>
        /// 标准数据格式
        /// </summary>
        /// <param name="code">返回结果，0为成功</param>
        /// <param name="msg">返回消息</param>
        /// <param name="jarr">返回的数据</param>
        /// <param name="count">返回数据长度</param>
        /// <returns></returns>
        public static JObject Result(int code,string msg,JArray jarr,int count=0)
        {
            JObject obj = new JObject();

            obj.Add("code", code);
            obj.Add("msg", msg);
            obj.Add("data", jarr);
            obj.Add("count", count);
            obj.Add("rettime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));

            return obj;
        }

        /// <summary>
        /// 返回传入Jobjcet格式的数据
        /// </summary>
        /// <param name="code"></param>
        /// <param name="msg"></param>
        /// <param name="obj"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static JObject Result(int code, string msg, JObject obj, int count = 1)
        {
            JArray arr = new JArray();
            arr.Add(obj);

            return Result(code, msg, arr, count);
        }

        /// <summary>
        /// 返回自定义格式的消息
        /// </summary>
        /// <param name="code"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static JObject Result(int code, string msg)
        {
            JArray arr = new JArray();
            return Result(code, msg, arr, 0);
        }

        /// <summary>
        /// 成功
        /// </summary>
        /// <returns></returns>
        public static JObject Success()
        {
            return Result(0, "");
        }

        /// <summary>
        /// 成功，带参数
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static JObject Success(string msg)
        {
            return Result(0, msg);
        }

        /// <summary>
        /// 成功，Jobject数据
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static JObject Success(JObject obj)
        {
            return Result(0, "", obj, 1);
        }

        /// <summary>
        /// 成功，JArray格式
        /// </summary>
        /// <param name="arr"></param>
        /// <returns></returns>
        public static JObject Success(JArray arr)
        {
            return Result(0, "", arr, 0);
        }

        /// <summary>
        /// 失败，必须带参数
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static JObject Error(string msg)
        {
            return Result(-1, msg);
        }

        /// <summary>
        /// 错误，带参数类型
        /// </summary>
        /// <param name="code"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static JObject Error(int code, string msg)
        {
            if (code == 0)
            {
                return Result(-1, msg); 
            }

            return Result(code, msg);
        }
    }
}
