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

# === P0 #1: .gitignore - prevent credential leak ===
fix_file('.gitignore', [
    ('_logs*.txt', '_logs*.txt\n.env\n!*.example\nappsettings.Production.json\n!appsettings.Production.json.example'),
])

# === P1 #2: appsettings.Production.json - enable HTTPS/HSTS ===
fix_file('appsettings.Production.json', [
    ('"Enabled": false', '"Enabled": true'),
])

# === P1 #3: GHA CI - add timeout-minutes ===
fix_file('.github/workflows/build.yml', [
    ('name: build-and-test\n    runs-on: ubuntu-latest',
     'name: build-and-test\n    runs-on: ubuntu-latest\n    timeout-minutes: 45'),
    ('- name: dotnet restore\n        run: dotnet restore XHDCRM3.sln',
     '- name: dotnet restore\n        run: dotnet restore XHDCRM3.sln\n        timeout-minutes: 10'),
    ('- name: dotnet build\n        run: dotnet build XHDCRM3.sln --no-restore --configuration Release --verbosity normal',
     '- name: dotnet build\n        run: dotnet build XHDCRM3.sln --no-restore --configuration Release --verbosity normal\n        timeout-minutes: 15'),
    ('- name: dotnet test\n        run: dotnet test 7.Test/XHD.Core.Tests/XHD.Core.Tests.csproj --no-build --configuration Release --verbosity normal --logger "trx;LogFileName=results.trx"',
     '- name: dotnet test\n        run: dotnet test 7.Test/XHD.Core.Tests/XHD.Core.Tests.csproj --no-build --configuration Release --verbosity normal --logger "trx;LogFileName=results.trx"\n        timeout-minutes: 20'),
    ('- name: Docker compose build\n        run: |\n          docker compose build --no-cache',
     '- name: Docker compose build\n        run: |\n          docker compose build\n        timeout-minutes: 20'),
])

# === P1 #4: docker-compose.mssql.yml - DB port bind to 127.0.0.1 ===
fix_file('docker-compose.mssql.yml', [
    ('- "${DB_HOST_PORT:-1433}:1433"', '- "${DB_HOST_PORT:-127.0.0.1:1433}:1433"'),
])

# === P1 #4 companion: .env.example - update defaults ===
fix_file('.env.example', [
    ('DB_HOST_PORT=1433', 'DB_HOST_PORT=127.0.0.1:1433'),
    ('DB_SA_PASSWORD=Your_Strong_Password_123', 'DB_SA_PASSWORD=CHANGE_ME_TO_A_STRONG_PASSWORD_16+'),
])

print('P0+P1 fixes complete')