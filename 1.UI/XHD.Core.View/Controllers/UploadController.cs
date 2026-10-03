
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using XHD.Core.IServices;
using XHD.Core.Models;
using System.Security.Claims;
using XHD.Core.Common;
using XHD.Core.View.Authorization;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class UploadController : Controller
    {
        private readonly ICRM_Customer_attaService _customerattaservice;
        private readonly ILogger<UploadController> _logger;

        public UploadController(ICRM_Customer_attaService customerattaservice, ILogger<UploadController> logger)
        {
            _customerattaservice = customerattaservice;
            _logger = logger;
        }

        [ButtonAuth("Message_News", "upload")]
        public async Task<string> Image()
        {
            byte[] buffer = Guid.NewGuid().ToByteArray();
            var out_trad_id = BitConverter.ToInt64(buffer, 0).ToString();


            var files = Request.Form.Files;
            if (files.Count == 0)
            {
                return "No File";
            }
            var file = files[0];
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (fileExtension == ".jpg" || fileExtension == ".png" || fileExtension == ".gif" || fileExtension == ".jpeg")
            {

            }
            else
            {
                return "File type error";
            }

            var UploadDir = $"Upload/Image/{DateTime.Now.ToString("yyyy-MM-dd")}";
            var fileDir = $"{Directory.GetCurrentDirectory()}/wwwroot/Upload/Image/{DateTime.Now.ToString("yyyy-MM-dd")}";
            if (!Directory.Exists(fileDir))
                Directory.CreateDirectory(fileDir);

            var filename = $"{out_trad_id}{fileExtension}";
            var fullpath = Path.Combine(fileDir, filename);
            using (var fs = new FileStream(fullpath, FileMode.Create, FileAccess.Write))
            {
                //file.CopyTo(fs);
                //fs.Close();
                await file.CopyToAsync(fs);
            }
            var url = $"../{UploadDir}/{filename}";

            JObject obj = new JObject();
            obj.Add("location", url);

            return obj.ToString();
        }

        [DisableRequestSizeLimit]
        [ButtonAuth("sysconfig", "fileup")]
        public async Task<string> FileUp()
        {
            byte[] buffer = Guid.NewGuid().ToByteArray();
            var out_trad_id = BitConverter.ToInt64(buffer, 0).ToString();


            var files = Request.Form.Files;
            if (files.Count == 0)
            {
                return "No File";
            }
            var file = files[0];
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (fileExtension == ".apk")
            {

            }
            else
            {
                return "File type error";
            }

            var UploadDir = $"Upload/File/{DateTime.Now.ToString("yyyy-MM-dd")}";
            var fileDir = $"{Directory.GetCurrentDirectory()}/wwwroot/Upload/File/{DateTime.Now.ToString("yyyy-MM-dd")}";
            if (!Directory.Exists(fileDir))
                Directory.CreateDirectory(fileDir);

            var filename = $"{out_trad_id}{fileExtension}";
            var fullpath = Path.Combine(fileDir, filename);
            using (var fs = new FileStream(fullpath, FileMode.Create, FileAccess.Write))
            {
                //file.CopyTo(fs);
                //fs.Close();
                await file.CopyToAsync(fs);
            }
            var url = $"../{UploadDir}/{filename}";

            JObject obj = new JObject();
            obj.Add("location", url);

            return obj.ToString();
        }


        /// <summary>
        /// 下载本地物理文件（指定文件名）
        /// </summary>
        /// <param name="fileName">客户端接收的文件名（如 "我的报告.pdf"）</param>
        /// <returns>文件流</returns>

        [ButtonAuth("CRM_Customer", "atta_download")]
        public async Task<IActionResult> DownloadCustomerAtta(string id)
        {
            id = Path.GetFileName(id);
            if (string.IsNullOrWhiteSpace(id) || id.Contains("..") || id.Contains("/") || id.Contains("\\"))
                return BadRequest("非法文件名");
            Expression<Func<CRM_Customer_atta, bool>> exp = a => a.id == id;

            var attadata = _customerattaservice.Grid(exp);

            if (attadata.count == 0)
            {
                return NotFound("文件不存在");
            }

            var data = attadata.data[0];

            //var url = '/upload/customer/' + data.cus_id + "/" + data.real_name;

            // 1. 服务器本地文件路径（确保路径正确，建议使用绝对路径）
            //string serverFilePath = Path.Combine(
            //    Directory.GetCurrentDirectory(),  // 项目根目录
            //    "/upload/customer/", data.cus_id, data.real_name  // 服务器存储的原始文件
            //);

            string safeCusId = Path.GetFileName(data.cus_id ?? "");
            string safeRealName = Path.GetFileName(data.real_name ?? "");
            if (string.IsNullOrWhiteSpace(safeCusId) || string.IsNullOrWhiteSpace(safeRealName))
                return NotFound("文件信息异常");
            string serverFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "upload", "customer", safeCusId, safeRealName);

            _logger.LogInformation("Server file path: {Path}", serverFilePath);

            // 2. 验证文件是否存在
            if (!System.IO.File.Exists(serverFilePath))
            {
                return NotFound("文件不存在");
            }

            // 2. 动态获取 Content-Type
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            if (!contentTypeProvider.TryGetContentType(serverFilePath, out string? contentType))
            {
                contentType = "application/octet-stream"; // 未知类型默认二进制流
            }

            // 3. 读取文件流（使用 FileStream 确保资源释放）
            var fileStream = new FileStream(serverFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

            // 4. 设置响应头：指定下载文件名（支持中文，需编码）
            var contentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                // 编码文件名，避免中文乱码
                FileNameStar = Uri.EscapeDataString(data.file_name),
                FileName = Uri.EscapeDataString(data.file_name)
            };
            Response.Headers.TryAdd(HeaderNames.ContentDisposition, contentDisposition.ToString());

            return File(fileStream, contentType, enableRangeProcessing: true); // enableRangeProcessing 支持断点续传
        }

        /// <summary>
        /// Sprint 7 #119 upload.cus_import：客户 Excel 导入上传。
        /// 对应 A 侧 Server.upload.cus_import（Server/upload.cs:90-99）：
        /// 保存到 ~/file/customer/Customer.xls（覆盖固定文件名），返回 "Customer.xls"。
        /// </summary>
        /// <param name="file">上传的客户 Excel 文件（IFormFile）</param>
        /// <returns>标准 XHDResult 字符串（msg 为落盘文件名）</returns>
        [DisableRequestSizeLimit]
        [HttpPost("cus_import")]
        [ButtonAuth("CRM_Customer", "cus_import")]
        public async Task<string> CusImport(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return XHDResult.Error("未选择文件").ToString();
            }

            var nowfileName = "Customer.xls";
            var relDir = "wwwroot/file/customer";
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), relDir);
            if (!Directory.Exists(fullDir))
            {
                Directory.CreateDirectory(fullDir);
            }
            var fullPath = Path.Combine(fullDir, nowfileName);

            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            return XHDResult.Success(nowfileName).ToString();
        }

        /// <summary>
        /// Sprint 7 #120 upload.contact_import：联系人 Excel 导入上传。
        /// 对应 A 侧 Server.upload.contact_import（Server/upload.cs:100-109）：
        /// 保存到 ~/file/contact/contact.xls（覆盖固定文件名），返回 "contact.xls"。
        /// </summary>
        /// <param name="file">上传的联系人 Excel 文件（IFormFile）</param>
        /// <returns>标准 XHDResult 字符串（msg 为落盘文件名）</returns>
        [DisableRequestSizeLimit]
        [HttpPost("contact_import")]
        [ButtonAuth("crm_contact", "contact_import")]
        public async Task<string> ContactImport(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return XHDResult.Error("未选择文件").ToString();
            }

            var nowfileName = "contact.xls";
            var relDir = "wwwroot/file/contact";
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), relDir);
            if (!Directory.Exists(fullDir))
            {
                Directory.CreateDirectory(fullDir);
            }
            var fullPath = Path.Combine(fullDir, nowfileName);

            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            return XHDResult.Success(nowfileName).ToString();
        }

        /// <summary>
        /// Sprint 10.38 员工头像上传（对应 A 侧 hr_employee 头像链路）。
        /// 与 A 版 base64 直存不同：复用 B 版 Image() 的 IFormFile 直传范式，
        /// 前端以 canvas 裁切为 120x120 正方形后上传，服务端落盘 wwwroot/Upload/Header/{日期}/。
        /// </summary>
        /// <returns>标准 XHDResult 字符串，成功时 data[0].url 为相对 wwwroot 的头像路径</returns>
        [HttpPost("HeadImg")]
        [ButtonAuth("hr_employee", "edit")]
        public async Task<string> HeadImg()
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return XHDResult.Error("未接收到文件！").ToString();
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLower();
            if (fileExtension != ".jpg" && fileExtension != ".jpeg" && fileExtension != ".png" && fileExtension != ".gif")
            {
                return XHDResult.Error("仅支持 jpg/png/gif！").ToString();
            }

            if (file.Length > 2 * 1024 * 1024)
            {
                return XHDResult.Error("图片不能超过 2MB！").ToString();
            }

            var UploadDir = $"Upload/Header/{DateTime.Now.ToString("yyyy-MM-dd")}";
            var fileDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", UploadDir);
            if (!Directory.Exists(fileDir))
                Directory.CreateDirectory(fileDir);

            var filename = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}_{new Random().Next(10000, 99999)}{fileExtension}";
            var fullpath = Path.Combine(fileDir, filename);
            using (var fs = new FileStream(fullpath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            var url = $"/{UploadDir}/{filename}";

            JObject obj = new JObject();
            obj.Add("url", url);

            return XHDResult.Success(obj).ToString();
        }
    }
}
