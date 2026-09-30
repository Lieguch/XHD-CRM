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
# Views P1 fixes
# ============================================================

# === P1-1: Account/Login.cshtml - remove inline script ===
fix_file('1.UI/XHD.Core.View/Views/Account/Login.cshtml', [
    ('<script>\n    $(document).ready(function () {', '<script>\n    $(document).ready(function () {'),
])

# Actually let me read the files first to understand the exact patterns
print("Need to read view files first for precise patterns")