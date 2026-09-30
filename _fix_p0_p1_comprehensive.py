#!/usr/bin/env python3
"""Sprint 10.38 P0/P1/P2 comprehensive fixes."""
import os, sys

BASE = r"D:\output\xhdcrm\work_sprint10.38"
FIXED = []
FAILED = []

def fix_file(relpath, replacements):
    fpath = os.path.join(BASE, relpath)
    if not os.path.exists(fpath):
        FAILED.append(f"{relpath}: FILE NOT FOUND")
        return
    with open(fpath, 'rb') as f:
        raw = f.read()
    bom = None
    if raw.startswith(b'\xef\xbb\xbf'):
        bom = b'\xef\xbb\xbf'
        content = raw[3:].decode('utf-8')
    elif raw.startswith(b'\xff\xfe'):
        bom = b'\xff\xfe'
        content = raw[2:].decode('utf-16-le')
    elif raw.startswith(b'\xfe\xff'):
        bom = b'\xfe\xff'
        content = raw[2:].decode('utf-16-be')
    else:
        content = raw.decode('utf-8')
    original = content
    for old, new in replacements:
        if old in content:
            content = content.replace(old, new, 1)
            print(f"  [OK] {relpath}")
        else:
            print(f"  [SKIP] {relpath}: not found: {old[:60]}")
    if content != original:
        if bom:
            with open(fpath, 'wb') as f:
                f.write(bom + content.encode('utf-8'))
        else:
            with open(fpath, 'w', encoding='utf-8') as f:
                f.write(content)
        FIXED.append(relpath)
        print(f"  [SAVED] {relpath}")
    else:
        FAILED.append(f"{relpath}: NO CHANGES")


# ============================================================
# 1. UploadController.cs - P0
# ============================================================
print("\n=== 1. UploadController.cs ===")
u = r"1.UI\XHD.Core.View\Controllers\UploadController.cs"

# Build replacements using triple-quoted strings to avoid escaping issues
r1_old = """        public async Task<string> Image()
        {
            byte[] buffer = Guid.NewGuid().ToByteArray();"""
r1_new = """        public async Task<string> Image()
        {
            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "upload|image"))
                return XHDResult.Error("\u65e0\u64cd\u4f5c\u6743\u9650").ToString();
            byte[] buffer = Guid.NewGuid().ToByteArray();"""

r2_old = """        [DisableRequestSizeLimit]
        public async Task<string> FileUp()
        {
            byte[] buffer = Guid.NewGuid().ToByteArray();"""
r2_new = """        [DisableRequestSizeLimit]
        public async Task<string> FileUp()
        {
            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "upload|fileup"))
                return XHDResult.Error("\u65e0\u64cd\u4f5c\u6743\u9650").ToString();
            byte[] buffer = Guid.NewGuid().ToByteArray();"""

r3_old = """        public IActionResult DownloadCustomerAtta(string id)
        {
            Expression<Func<CRM_Customer_atta, bool>> exp = a => a.id == id;"""
r3_new = """        public async Task<IActionResult> DownloadCustomerAtta(string id)
        {
            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "crm_customer|atta_download"))
                return XHDResult.Error("\u65e0\u64cd\u4f5c\u6743\u9650").ToString();
            id = Path.GetFileName(id);
            if (string.IsNullOrWhiteSpace(id) || id.Contains("..") || id.Contains("/") || id.Contains("\\"))
                return BadRequest("\u975e\u6cd5\u6587\u4ef6\u540d");
            Expression<Func<CRM_Customer_atta, bool>> exp = a => a.id == id;"""

# Path traversal fix - the original line has Chinese chars in f-string
r4_old = """            string serverFilePath = $"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/{data.cus_id}/{data.real_name}";"""
r4_new = """            string safeCusId = Path.GetFileName(data.cus_id ?? "");
            string safeRealName = Path.GetFileName(data.real_name ?? "");
            if (string.IsNullOrWhiteSpace(safeCusId) || string.IsNullOrWhiteSpace(safeRealName))
                return NotFound("\u6587\u4ef6\u4fe1\u606f\u5f02\u5e38");
            string serverFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "upload", "customer", safeCusId, safeRealName);"""

r5_old = """        [DisableRequestSizeLimit]
        [HttpPost("cus_import")]
        public async Task<string> CusImport(IFormFile file)
        {
            if (file == null || file.Length == 0)"""
r5_new = """        [DisableRequestSizeLimit]
        [HttpPost("cus_import")]
        public async Task<string> CusImport(IFormFile file)
        {
            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "crm_customer|cus_import"))
                return XHDResult.Error("\u65e0\u64cd\u4f5c\u6743\u9650").ToString();
            if (file == null || file.Length == 0)"""

r6_old = """        [DisableRequestSizeLimit]
        [HttpPost("contact_import")]
        public async Task<string> ContactImport(IFormFile file)
        {
            if (file == null || file.Length == 0)"""
r6_new = """        [DisableRequestSizeLimit]
        [HttpPost("contact_import")]
        public async Task<string> ContactImport(IFormFile file)
        {
            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "crm_contact|contact_import"))
                return XHDResult.Error("\u65e0\u64cd\u4f5c\u6743\u9650").ToString();
            if (file == null || file.Length == 0)"""

fix_file(u, [
    ("using XHD.Core.Common;",
     "using System.Security.Claims;\nusing XHD.Core.Common;"),
    ("        private readonly ILogger<UploadController> _logger;",
     "        private readonly IDBAuthService _dBAuthService;\n        private readonly ILogger<UploadController> _logger;"),
    ("""        public UploadController(ICRM_Customer_attaService customerattaservice, ILogger<UploadController> logger)
        {
            _customerattaservice = customerattaservice;
            _logger = logger;""",
     """        public UploadController(ICRM_Customer_attaService customerattaservice, IDBAuthService dBAuthService, ILogger<UploadController> logger)
        {
            _customerattaservice = customerattaservice;
            _dBAuthService = dBAuthService;
            _logger = logger;"""),
    (r1_old, r1_new),
    (r2_old, r2_new),
    (r3_old, r3_new),
    (r4_old, r4_new),
    (r5_old, r5_new),
    (r6_old, r6_new),
])

# ============================================================
# 2. AccountController.cs - P1 SignOut await
# ============================================================
print("\n=== 2. AccountController.cs ===")
a = r"1.UI\XHD.Core.View\Controllers\AccountController.cs"
fix_file(a, [
    ("""        public string SignOut()
        {
            _ = HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return XHDResult.Success().ToString();
        }""",
     """        public async Task<string> SignOut()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return XHDResult.Success().ToString();
        }"""),
])

# ============================================================
# 3. CustomerBatchController.cs - P1 transaction
# ============================================================
print("\n=== 3. CustomerBatchController.cs ===")
c = r"1.UI\XHD.Core.View\Controllers\CustomerBatchController.cs"
loop_old = """                foreach (var id in ids)
                {
                    await _fsql.Update<CRM_Customer>().Set(a => a.emp_id == model.new_emp_id).Where(a => a.id == id).ExecuteAffrowsAsync();

                    // 4. 记录日志
                    Sys_log logmodels = new Sys_log();

                    logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                    logmodels.EventType = "[\u5ba2\u6237]\u6279\u91cf\u8f6c\u79fb";
                    logmodels.EventID = model.id;
                    logmodels.cus_id = id;
                    logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                    logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                    logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                    logmodels.EventDate = DateTime.Now;
                    logmodels.Log_Content = $"\u5c06\u5ba2\u6237{id}\u4ece\u5458\u5de5 {model.old_emp_id} \u8f6c\u79fb\u7ed9\u5458\u5de5 {model.new_emp_id}";

                    await _logService.UpdateLog(logmodels);
                }"""

loop_new = """                using var uow = _fsql.CreateUnitOfWork();
                try
                {
                    foreach (var id in ids)
                    {
                        await _fsql.Update<CRM_Customer>().Set(a => a.emp_id == model.new_emp_id).Where(a => a.id == id).ExecuteAffrowsAsync();

                        Sys_log logmodels = new Sys_log();
                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[\u5ba2\u6237]\u6279\u91cf\u8f6c\u79fb";
                        logmodels.EventID = model.id;
                        logmodels.cus_id = id;
                        logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                        logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                        logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        logmodels.EventDate = DateTime.Now;
                        logmodels.Log_Content = $"\u5c06\u5ba2\u6237{id}\u4ece\u5458\u5de5 {model.old_emp_id} \u8f6c\u79fb\u7ed9\u5458\u5de5 {model.new_emp_id}";

                        await _logService.UpdateLog(logmodels);
                    }
                    await uow.CommitAsync();
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync();
                    return XHDResult.Error($"\u6279\u91cf\u8f6c\u79fb\u5931\u8d25: {ex.Message}").ToString();
                }"""

fix_file(c, [(loop_old, loop_new)])

# ============================================================
# 4. SysInfo/Index.cshtml - P1 eval() XSS
# ============================================================
print("\n=== 4. SysInfo/Index.cshtml ===")
fix_file(r"1.UI\XHD.Core.View\Views\SysInfo\Index.cshtml", [
    ("                    var infos = eval('(' + text + ')');",
     "                    var infos = JSON.parse(text);"),
])

# ============================================================
# 5. SMS/Report.cshtml - P1 @Html.Raw XSS
# ============================================================
print("\n=== 5. SMS/Report.cshtml ===")
fix_file(r"1.UI\XHD.Core.View\Views\SMS\Report.cshtml", [
    ("""        var smsid = "@Html.Raw(smsid)";""",
     """        var smsid = @System.Text.Json.JsonSerializer.Serialize(smsid);"""),
])

# ============================================================
# 6. Account/Index.cshtml - P1 AES_Key + P3 CORS
# ============================================================
print("\n=== 6. Account/Index.cshtml ===")
v = r"1.UI\XHD.Core.View\Views\Account\Index.cshtml"
fix_file(v, [
    # Remove ineffective CORS meta tag (line 8)
    ("""<meta http-equiv="Access-Control-Allow-Origin" content="*">""", ""),
    # AES_Key: read from hidden field instead of server injection
    ("""                var AES_Key = '@ViewBag.AES_Key';""",
     """                var AES_Key = document.getElementById('aes_key').value;"""),
])

# ============================================================
# 7. XHD.js - P1 ECB -> CBC parity fix
# ============================================================
print("\n=== 7. XHD.js ===")
fix_file(r"1.UI\XHD.Core.View\wwwroot\JS\XHD.js", [
    ("""    // ECB模式加密
    var encrypted = CryptoJS.AES.encrypt(plainText, key, {
        mode: CryptoJS.mode.ECB,
        padding: CryptoJS.pad.Pkcs7
    });""",
     """    // CBC模式加密 — IV从密钥后缀推导（与C# AESEncrypt.cs一致）
    var ivStr = secretKey.substring(Math.max(0, secretKey.length - 16));
    var iv = CryptoJS.enc.Utf8.parse(ivStr);
    var encrypted = CryptoJS.AES.encrypt(plainText, key, {
        iv: iv,
        mode: CryptoJS.mode.CBC,
        padding: CryptoJS.pad.Pkcs7
    });"""),
])

# ============================================================
# SUMMARY
# ============================================================
print("\n" + "=" * 60)
print(f"FIXED: {len(FIXED)} files")
for f in FIXED:
    print(f"  \u2713 {f}")
if FAILED:
    print(f"\nFAILED: {len(FAILED)} items")
    for f in FAILED:
        print(f"  \u2717 {f}")
print("=" * 60)
