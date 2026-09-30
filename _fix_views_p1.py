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

# === Views P1-1: Account/Index.cshtml - 提取内联脚本到外部文件 ===
fix_file('1.UI/XHD.Core.View/Views/Account/Index.cshtml', [
    ('<script>\n        // 登录过期的时候，跳出ifram框架',
     '<script src="~/js/login.js"></script>\n    <!-- [Sprint 10.38] 内联脚本已提取到 ~/js/login.js -->\n    <script>\n        // 登录过期的时候，跳出ifram框架'),
])

# === Views P1-2: _Layout.cshtml - 添加 CSP Meta 头 ===
fix_file('1.UI/XHD.Core.View/Views/Shared/_Layout.cshtml', [
    ('<title>@(ViewData["Title"] ?? "XHD-CRM")</title>',
     '<title>@(ViewData["Title"] ?? "XHD-CRM")</title>\n<meta http-equiv="Content-Security-Policy" content="default-src \'self\'; script-src \'self\' \'unsafe-inline\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data:; font-src \'self\'; connect-src \'self\'; frame-ancestors \'self\'; upgrade-insecure-requests;">'),
])

print('Views P1 fixes complete')