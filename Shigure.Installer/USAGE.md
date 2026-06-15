# 安装器使用指南

## 🎯 功能说明

`Shigure.Installer` 用于一键生成「改名后的定制版」Shigure 程序包。它会：

1. ✏️ 把程序集名称从 `Shigure` 改为你指定的名称（如 `MyWowHelper.exe`）
2. 🔨 自动编译并发布（Release）
3. 📦 输出一个干净、可直接分发的程序文件夹（包含 `config/keymap/module` 配置内容）

> **核心特性：全程在系统临时目录里构建，绝不改动主项目的 `.csproj`、源码、`bin/`、`obj/`。**
> 旧版安装器靠「改写 csproj → 还原」「删除 bin/obj」工作，一旦中途出错就会损坏主项目（例如把 `AssemblyName` 写坏）。新版彻底不碰主项目，构建失败也只影响临时目录。

---

## 📋 前置要求

- 已安装 **.NET 10 SDK**（`dotnet --version` 可用）
- Windows 系统
- 安装器需位于 Shigure 仓库的 `Shigure.Installer` 子目录内运行（它会自动向上定位主项目 `Shigure.csproj`）

---

## 🚀 快速开始（图形界面）

### 1. 构建安装器

```powershell
cd Shigure.Installer
dotnet build -c Release
```

安装器位于：`bin\Release\net10.0-windows\Shigure.Installer.exe`

### 2. 运行并填写

双击 `Shigure.Installer.exe`：

1. **程序名称**：自定义名称（如 `MyWowHelper`）
   - 仅支持英文字母、数字、下划线，且不能以数字开头
2. **输出路径**：选择输出文件夹，留空默认桌面
   - 最终会输出到 `<输出路径>\<程序名称>\`
3. 点击「**开始构建**」，日志区会实时显示进度

---

## 💻 命令行模式（可脚本化 / 批量生成）

带参数运行即进入命令行模式，无参数则打开图形界面：

```powershell
Shigure.Installer.exe <程序名> [输出目录]
```

| 参数          | 说明                                                       |
| ------------- | ---------------------------------------------------------- |
| `<程序名>`    | 必填，仅字母/数字/下划线，不能以数字开头                   |
| `[输出目录]`  | 可选，默认桌面；最终输出到 `<输出目录>\<程序名>`           |
| `--help`      | 显示用法                                                   |

示例：

```powershell
.\bin\Release\net10.0-windows\Shigure.Installer.exe DruidHelper D:\Games
```

退出码：`0` 成功，非 `0` 失败（失败原因写入标准错误）。

---

## 📂 输出结构

```
MyWowHelper/
├── MyWowHelper.exe            # 主程序（已改名）
├── MyWowHelper.dll
├── MyWowHelper.deps.json
├── MyWowHelper.runtimeconfig.json
├── *.dll                      # 运行时依赖
├── config/                    # 配置文件
├── keymap/                    # 按键映射文件
└── module/                    # 模块文件
```

---

## ⚙️ 构建流程

```
1. 向上定位主项目 Shigure.csproj
2. 把主项目源码复制到 %TEMP%\ShigureInstaller\<随机>\src
   （排除 bin/obj/.git/.vs/.vscode/cache/artifacts/tmp/Shigure.Installer 等）
3. 在临时副本上执行：
     dotnet publish src\Shigure.csproj -c Release -o <publish> -p:AssemblyName=<程序名>
   —— 用命令行属性覆盖程序集名称，不改写任何文件
4. 把发布产物复制到输出目录（包含 config/keymap/module 配置内容）
5. 删除临时目录
```

### 为什么只改 `AssemblyName`、不改 `RootNamespace`

代码里的命名空间始终是 `namespace Shigure`，而窗体资源（如 `UI/MainForm.resx`）按 `RootNamespace`（= `Shigure`）生成清单名。若同时改动 `RootNamespace`，运行时会出现资源加载错位。因此只覆盖程序集名称：`.exe/.dll` 改名，代码与资源不受影响。

---

## ⚠️ 注意事项

1. **不影响开发环境**：构建只在临时目录进行，主项目的 `.csproj/源码/bin/obj` 不会被修改。
2. **输出目录会被重建**：若同名目录已存在会先删除再生成；请勿让目标程序处于运行中或在资源管理器里打开，否则删除会因占用失败（安装器会给出明确提示）。
3. **命名空间不变**：生成程序内部仍为 `namespace Shigure`，仅程序集（文件）名改变。

---

## 🔧 故障排查

### 提示「无法启动 dotnet」

确认 .NET 10 SDK 已安装且在 PATH 中：

```powershell
dotnet --version
```

### 提示「未能找到主项目 Shigure.csproj」

安装器从自身所在位置逐级向上查找。请确保它位于仓库的 `Shigure.Installer` 子目录内（即 `Shigure\Shigure.Installer\...`）运行，不要把 exe 单独拷到别处。

### 提示「无法清理已存在的输出目录」

目标文件夹里有文件被占用：关闭正在运行的同名程序、关掉在资源管理器中打开的该目录，然后重试。

### `dotnet publish 失败`

日志区/标准错误会附带前若干条编译错误。常见为主项目本身存在编译错误——先在仓库根目录确认 `dotnet build .\Shigure.csproj` 能干净通过。
