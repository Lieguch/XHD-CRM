import os

def fix_file(path, replacements):
    with open(path, 'r', encoding='utf-8-sig') as f:
        content = f.read()
    changed = False
    for old, new in replacements:
        if old in content:
            content = content.replace(old, new)
            changed = True
    if changed:
        tmp = path + '.tmp'
        with open(tmp, 'w', encoding='utf-8-sig') as f:
            f.write(content)
        os.replace(tmp, path)
        print(f'  FIXED: {os.path.basename(path)}')
    else:
        print(f'  SKIP (no match): {os.path.basename(path)}')
    return changed

# ============================================================
# P0 Fix: UploadController - inject IDBAuthService + GetAuth
# ============================================================
fix_file('1.UI/XHD.Core.View/Controllers/UploadController.cs', [
    # Add field
    ('        private readonly ISys_infoService _sysInfoService;',
     '        private readonly ISys_infoService _sysInfoService;\n        private readonly IDBAuthService _dBAuthService;'),
    # Update constructor
    ('        public UploadController(ILogger<UploadController> logger, ISale_contract_attaService attaservice, ISys_infoService sysInfoService)',
     '        public UploadController(ILogger<UploadController> logger, ISale_contract_attaService attaservice, ISys_infoService sysInfoService, IDBAuthService dBAuthService)'),
    ('            _sysInfoService = sysInfoService;',
     '            _sysInfoService = sysInfoService;\n            _dBAuthService = dBAuthService;'),
    # DownloadFile - add GetAuth
    ('        public async Task<string> DownloadFile(string id, string type)\n        {\n            string fileName = string.Empty;',
     '        public async Task<string> DownloadFile(string id, string type)\n        {\n            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "file|download"))\n                return XHDResult.Error("无操作权限").ToString();\n            string fileName = string.Empty;'),
    # DownloadCustomerAtta - add GetAuth + path traversal fix
    ('        public async Task<string> DownloadCustomerAtta(string id)\n        {\n            // 1. 获取文件名',
     '        public async Task<string> DownloadCustomerAtta(string id)\n        {\n            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "file|download"))\n                return XHDResult.Error("无操作权限").ToString();\n            // 1. 获取文件名'),
    ('            string cus_id = Request.Form["cus_id"];\n            string real_name = Request.Form["name"];\n            string filePath = $"/wwwroot/upload/customer/{cus_id}/{real_name}";',
     '            string cus_id = Request.Form["cus_id"];\n            string real_name = Request.Form["name"];\n            // Sprint 10.38: 路径遍历防护\n            cus_id = Path.GetFileName(cus_id);\n            real_name = Path.GetFileName(real_name);\n            if (cus_id.Contains("..") || real_name.Contains("..") || cus_id.Contains("/") || real_name.Contains("/") || cus_id.Contains("\\"))\n                return XHDResult.Error("参数非法").ToString();\n            string filePath = $"/wwwroot/upload/customer/{cus_id}/{real_name}";'),
    # DownloadContractAtta - add GetAuth + path traversal fix
    ('        public async Task<string> DownloadContractAtta(string id)\n        {\n            string fileName = string.Empty;',
     '        public async Task<string> DownloadContractAtta(string id)\n        {\n            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "file|download"))\n                return XHDResult.Error("无操作权限").ToString();\n            string fileName = string.Empty;'),
    ('            string contract_id = Request.Form["contract_id"];\n            string real_name = Request.Form["name"];\n            string filePath = $"/wwwroot/upload/contract/{contract_id}/{real_name}";',
     '            string contract_id = Request.Form["contract_id"];\n            string real_name = Request.Form["name"];\n            // Sprint 10.38: 路径遍历防护\n            contract_id = Path.GetFileName(contract_id);\n            real_name = Path.GetFileName(real_name);\n            if (contract_id.Contains("..") || real_name.Contains("..") || contract_id.Contains("/") || real_name.Contains("/") || contract_id.Contains("\\"))\n                return XHDResult.Error("参数非法").ToString();\n            string filePath = $"/wwwroot/upload/contract/{contract_id}/{real_name}";'),
])

# ============================================================
# P0 Fix: CustomerAttaController - path traversal + GetAuth
# ============================================================
fix_file('1.UI/XHD.Core.View/Controllers/CustomerAttaController.cs', [
    # Upload - path traversal fix (cus_id + fileName)
    ('                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/{Request.Form["guid"]}-{Request.Form["id"]}/");',
     '                        // Sprint 10.38: 路径遍历防护\n                        string guid = Path.GetFileName(Request.Form["guid"]);\n                        string attId = Path.GetFileName(Request.Form["id"]);\n                        if (guid.Contains("..") || attId.Contains("..") || guid.Contains("/") || attId.Contains("/") || guid.Contains("\\"))\n                            return XHDResult.Error("参数非法").ToString();\n                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/{guid}-{attId}/");'),
    ('                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/{customer_id}/");',
     '                        // Sprint 10.38: 路径遍历防护\n                        customer_id = Path.GetFileName(customer_id);\n                        if (customer_id.Contains("..") || customer_id.Contains("/") || customer_id.Contains("\\\\"))\n                            return XHDResult.Error("参数非法").ToString();\n                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/{customer_id}/");'),
    ('                        string fileName = Request.Form["name"];//文件名\n                        string fileExt = Path.GetExtension(fileName);//获取文件后缀',
     '                        string fileName = Request.Form["name"];//文件名\n                        fileName = Path.GetFileName(fileName); // Sprint 10.38: 路径遍历防护\n                        if (fileName.Contains("..")) return XHDResult.Error("参数非法").ToString();\n                        string fileExt = Path.GetExtension(fileName);//获取文件后缀'),
    # Meger - path traversal fix
    ('            var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/");',
     '            // Sprint 10.38: 路径遍历防护\n            customer_id = Path.GetFileName(customer_id);\n            if (customer_id.Contains("..") || customer_id.Contains("/") || customer_id.Contains("\\"))\n                return XHDResult.Error("参数非法").ToString();\n            var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/customer/");'),
    ('                var temporary = Path.GetDirectoryName($"{basePath}/{Path.GetFileName(Request.Form["guid"])}-{Path.GetFileName(Request.Form["id"])}//");',
     '                string guid = Path.GetFileName(Request.Form["guid"]);\n                string attId = Path.GetFileName(Request.Form["id"]);\n                if (guid.Contains("..") || attId.Contains(".."))\n                    return XHDResult.Error("参数非法").ToString();\n                var temporary = Path.GetDirectoryName($"{basePath}/{guid}-{attId}/");'),
    ('                string fileName = Path.GetFileName(Request.Form["name"]);//文件名（净化）',
     '                string fileName = Path.GetFileName(Request.Form["name"]);//文件名（净化）\n                if (fileName.Contains("..")) return XHDResult.Error("参数非法").ToString();'),
    # Del - GetAuth + exception handling
    ('        public async Task<string> Del(string id)\n        {\n            Expression<Func<CRM_Customer_atta, bool>> exp = a => a.id == id;',
     '        public async Task<string> Del(string id)\n        {\n            if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "crm_customer|atta_del"))\n                return XHDResult.Error("无操作权限").ToString();\n            Expression<Func<CRM_Customer_atta, bool>> exp = a => a.id == id;'),
])

# ============================================================
# P0 Fix: SaleContractAttaController - path traversal + exception handling
# ============================================================
fix_file('1.UI/XHD.Core.View/Controllers/SaleContractAttaController.cs', [
    # Upload - path traversal fix
    ('                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/{Request.Form["guid"]}-{Request.Form["id"]}/");',
     '                        // Sprint 10.38: 路径遍历防护\n                        string guid = Path.GetFileName(Request.Form["guid"]);\n                        string attId = Path.GetFileName(Request.Form["id"]);\n                        if (guid.Contains("..") || attId.Contains("..") || guid.Contains("/") || attId.Contains("/") || guid.Contains("\\"))\n                            return XHDResult.Error("参数非法").ToString();\n                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/{guid}-{attId}/");'),
    ('                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/{ Request.Form["contract_id"] }/");',
     '                        // Sprint 10.38: 路径遍历防护\n                        string contract_id = Path.GetFileName(Request.Form["contract_id"]);\n                        if (contract_id.Contains("..") || contract_id.Contains("/") || contract_id.Contains("\\"))\n                            return XHDResult.Error("参数非法").ToString();\n                        var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/{contract_id}/");'),
    ('                        string fileName = Request.Form["name"];//文件名\n                        string fileExt = Path.GetExtension(fileName);//获取文件后缀',
     '                        string fileName = Request.Form["name"];//文件名\n                        fileName = Path.GetFileName(fileName); // Sprint 10.38: 路径遍历防护\n                        if (fileName.Contains("..")) return XHDResult.Error("参数非法").ToString();\n                        string fileExt = Path.GetExtension(fileName);//获取文件后缀'),
    # Meger - path traversal + exception handling
    ('            var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/");',
     '            // Sprint 10.38: 路径遍历防护\n            string contract_id = Path.GetFileName(Request.Form["contract_id"]);\n            if (contract_id.Contains("..") || contract_id.Contains("/") || contract_id.Contains("\\"))\n                return XHDResult.Error("参数非法").ToString();\n            var basePath = Path.GetDirectoryName($"{Directory.GetCurrentDirectory()}/wwwroot/upload/contract/");'),
    ('                var temporary = Path.GetDirectoryName($"{basePath}/{ Request.Form["guid"] }-{Request.Form["id"]}/");//临时文件夹',
     '                string guid = Path.GetFileName(Request.Form["guid"]);\n                string attId = Path.GetFileName(Request.Form["id"]);\n                if (guid.Contains("..") || attId.Contains(".."))\n                    return XHDResult.Error("参数非法").ToString();\n                var temporary = Path.GetDirectoryName($"{basePath}/{guid}-{attId}/");//临时文件夹'),
    ('                string fileName = Request.Form["name"];//文件名\n                string fileExt = Path.GetExtension(fileName);//获取文件后缀',
     '                string fileName = Path.GetFileName(Request.Form["name"]);//文件名\n                if (fileName.Contains("..")) return XHDResult.Error("参数非法").ToString();\n                string fileExt = Path.GetExtension(fileName);//获取文件后缀'),
    ('            catch (Exception ex)\n            {\n                NLogger.WriteLog("file_err_", ex.ToString());\n            }',
     '            catch (Exception ex)\n            {\n                _logger?.LogError(ex, "[SaleContractAttaController] Meger failed: {Message}", ex.Message);\n            }'),
    # Del - exception handling
    ('            catch\n            {\n\n            }',
     '            catch (Exception ex)\n            {\n                _logger?.LogWarning(ex, "[SaleContractAttaController] Del failed: {Message}", ex.Message);\n            }'),
])

print('P0 fixes complete')