# Shigure 安装器

一键生成「改名后的定制版」Shigure 程序包。详细说明见 [USAGE.md](USAGE.md)。

## 使用方法

### 图形界面

1. 运行 `Shigure.Installer.exe`
2. 输入自定义程序名称（字母/数字/下划线，不能以数字开头）
3. 选择输出路径（留空默认桌面）
4. 点击「开始构建」

### 命令行

```powershell
Shigure.Installer.exe <程序名> [输出目录]
```

## 工作原理

安装器**全程在系统临时目录里构建**，不会修改主项目的 `.csproj`、源码、`bin/`、`obj/`：

1. 向上定位主项目 `Shigure.csproj`
2. 把源码复制到临时目录（排除 `bin/obj` 等）
3. 在副本上执行 `dotnet publish -c Release -p:AssemblyName=<程序名>`（命令行覆盖程序集名称，不改写文件）
4. 把产物复制到输出目录（包含 `config/keymap/module` 配置内容）
5. 删除临时目录

只覆盖 `AssemblyName`、保持 `RootNamespace=Shigure`，以免窗体资源加载错位；生成程序内部命名空间仍为 `Shigure`，仅文件名改变。

## 构建安装器

```powershell
cd Shigure.Installer
dotnet build -c Release
```

生成于：`bin\Release\net10.0-windows\Shigure.Installer.exe`

## 注意事项

- 需要 .NET 10 SDK
- 安装器须在仓库的 `Shigure.Installer` 子目录内运行（用于定位主项目）
- 输出同名目录会被重建；构建前请关闭正在运行的目标程序
