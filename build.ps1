param(
    [string]$ClassIslandDir = $env:CLASSISLAND_DIR,
    [string]$DeployTo,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ClassIslandDir) -or -not (Test-Path (Join-Path $ClassIslandDir "ClassIsland.Core.dll"))) {
    throw "请先设置环境变量 CLASSISLAND_DIR（指向 ClassIsland 安装目录，目录里应有 ClassIsland.Core.dll），或用 -ClassIslandDir 传入。"
}
$env:CLASSISLAND_DIR = $ClassIslandDir

function Step([string]$text) { Write-Host "`n$text" -ForegroundColor Cyan }
function Run([string]$file, [string[]]$arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "命令失败：$file $($arguments -join ' ')" }
}

Step "1/5 编译插件…"
Run "dotnet" @("build", "$root\src\RandomDraw.csproj", "-c", "Release", "--nologo", "-v", "q")

Step "2/5 测试：抽选规则…"
Run "dotnet" @("run", "--project", "$root\tests\store\DrawStoreTests.csproj", "-c", "Release")

Step "3/5 测试：行动与提醒…"
Run "dotnet" @("run", "--project", "$root\tests\harness\ActionHarness.csproj", "-c", "Release")

Step "4/5 测试：插件注册…"
Run "dotnet" @("run", "--project", "$root\tests\registration\RegistrationHarness.csproj", "-c", "Release")

Step "5/5 测试：设置页 UI 冒烟…"
Run "dotnet" @("run", "--project", "$root\tests\ui\UiSmoke.csproj", "-c", "Release")

if ($DeployTo) {
    $pluginDir = Join-Path $DeployTo "leaf5049.randomdraw"
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    $buildDir = "$root\src\bin\Release\net8.0"
    Copy-Item "$buildDir\Leaf5049.RandomDraw.dll" -Destination $pluginDir -Force
    Copy-Item "$root\src\manifest.yml", "$root\src\icon.png", "$root\src\README.md" -Destination $pluginDir -Force
    # 不覆盖已有的 名单.txt / 抽选数据.json
    $namesFile = Join-Path $pluginDir "名单.txt"
    if (-not (Test-Path $namesFile)) { Copy-Item "$root\src\名单.txt" -Destination $namesFile }
    Write-Host "`n已部署到 $pluginDir（重启 ClassIsland 生效）" -ForegroundColor Green
}

if ($Package) {
    $dist = "$root\dist"
    $stage = Join-Path $env:TEMP ("leaf5049-pkg-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path (Join-Path $stage "leaf5049.randomdraw"), $dist -Force | Out-Null
    Copy-Item "$root\src\bin\Release\net8.0\Leaf5049.RandomDraw.dll", "$root\src\manifest.yml", "$root\src\icon.png", "$root\src\README.md", "$root\src\名单.txt" -Destination (Join-Path $stage "leaf5049.randomdraw")
    $pkg = Join-Path $dist "leaf5049.randomdraw.cipx"
    if (Test-Path $pkg) { Remove-Item $pkg -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $pkg, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Remove-Item $stage -Recurse -Force
    Write-Host "`n安装包：$pkg" -ForegroundColor Green
}

Write-Host "`n全部完成 ✔" -ForegroundColor Green
