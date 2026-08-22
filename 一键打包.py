from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path
from xml.sax.saxutils import escape as escape_xml


OLD_NAME = "Shigure"
ADDON_OLD_NAME = "Fuyutsui"
OLD_NAMES = (OLD_NAME, ADDON_OLD_NAME)
PROTECTED_TEXTS = (
    "https://www.shigure.club",
    "访问 Shigure 官网，浏览并获取可用模块",
)

SKIP_DIRS = {
    ".git",
    ".vs",
    ".vscode",
    ".agents",
    ".claude",
    "__pycache__",
    "Obsidian",
    "artifacts",
    "bin",
    "cache",
    "obj",
}

TEXT_EXTENSIONS = {
    ".bat",
    ".cmd",
    ".config",
    ".cs",
    ".csproj",
    ".editorconfig",
    ".gitignore",
    ".json",
    ".lua",
    ".md",
    ".props",
    ".ps1",
    ".py",
    ".resx",
    ".manifest",
    ".sln",
    ".slnx",
    ".targets",
    ".toc",
    ".txt",
    ".xaml",
    ".xml",
    ".yaml",
    ".yml",
}

NAME_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_]*$")
NAME_REPLACEMENT_PATTERN = re.compile(
    "|".join(re.escape(name) for name in OLD_NAMES),
    re.IGNORECASE,
)
SLASH_COMMAND_PATTERN = re.compile(r"/fu\b", re.IGNORECASE)
TARGET_FRAMEWORK_PATTERN = re.compile(
    r"<TargetFramework>net(?P<major>\d+)\.(?P<minor>\d+)(?:-[^<]+)?</TargetFramework>",
    re.IGNORECASE,
)
DOTNET_INSTALL_SCRIPT_URL = "https://dot.net/v1/dotnet-install.ps1"


def configure_console() -> None:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def get_app_paths() -> tuple[Path, Path]:
    if getattr(sys, "frozen", False):
        exe_path = Path(sys.executable).resolve()
        return exe_path.parent, exe_path

    script_path = Path(__file__).resolve()
    return script_path.parent, script_path


def ask_new_name() -> str:
    while True:
        new_name = input("请输入新的项目名称（英文开头，只能包含英文字母/数字/下划线）: ").strip()
        if NAME_PATTERN.fullmatch(new_name):
            return new_name
        print("名称格式不正确：必须以英文字母开头，只能包含英文字母、数字、下划线。")


def ask_company_name() -> str:
    while True:
        company_name = input("请输入公司名称（将自动添加 Corporation 后缀）: ").strip()
        if company_name and "\n" not in company_name and "\r" not in company_name:
            return f"{company_name} Corporation"
        print("公司名称不能为空，且不能包含换行。")


def update_company_name(project_path: Path, company_name: str) -> None:
    result = read_text(project_path)
    if result is None:
        raise RuntimeError(f"无法读取项目文件: {project_path}")

    project_text, encoding = result
    updated_text, replacements = re.subn(
        r"(<Company>).*?(</Company>)",
        lambda match: f"{match.group(1)}{escape_xml(company_name)}{match.group(2)}",
        project_text,
        count=1,
        flags=re.IGNORECASE | re.DOTALL,
    )
    if replacements == 0:
        raise RuntimeError(f"项目文件中找不到 <Company> 配置: {project_path}")

    project_path.write_text(updated_text, encoding=encoding, newline="")


def is_in_skipped_dir(path: Path, root: Path) -> bool:
    rel_parts = path.relative_to(root).parts
    return any(part in SKIP_DIRS for part in rel_parts)


def iter_text_files(root: Path, script_path: Path):
    for dirpath, dirnames, filenames in os.walk(root):
        current_dir = Path(dirpath)
        dirnames[:] = [name for name in dirnames if name not in SKIP_DIRS]

        for filename in filenames:
            path = current_dir / filename
            if path == script_path:
                continue
            if is_in_skipped_dir(path, root):
                continue
            if path.suffix.lower() not in TEXT_EXTENSIONS and filename.lower() not in TEXT_EXTENSIONS:
                continue
            yield path


def read_text(path: Path) -> tuple[str, str] | None:
    for encoding in ("utf-8", "gbk"):
        try:
            return path.read_text(encoding=encoding), encoding
        except UnicodeDecodeError:
            continue
    print(f"跳过无法识别编码的文件: {path}")
    return None


def match_name_case(old_name: str, new_name: str) -> str:
    if old_name.isupper():
        return new_name.upper()
    if old_name.islower():
        return new_name.lower()
    if old_name[:1].isupper() and old_name[1:].islower():
        return new_name[:1].upper() + new_name[1:]
    return new_name


def replace_names(value: str, new_name: str) -> str:
    protected_ranges = [
        match.span()
        for protected_text in PROTECTED_TEXTS
        for match in re.finditer(re.escape(protected_text), value)
    ]

    def replace_match(match: re.Match[str]) -> str:
        if any(start <= match.start() < end for start, end in protected_ranges):
            return match.group(0)
        return match_name_case(match.group(0), new_name)

    return NAME_REPLACEMENT_PATTERN.sub(
        replace_match,
        value,
    )


def replace_text(value: str, new_name: str) -> str:
    value = replace_names(value, new_name)
    slash_command = f"/{new_name[:2].lower()}"
    return SLASH_COMMAND_PATTERN.sub(slash_command, value)


def contains_text_replacement(value: str) -> bool:
    return bool(
        NAME_REPLACEMENT_PATTERN.search(value)
        or SLASH_COMMAND_PATTERN.search(value)
    )


def collect_replacements(root: Path, script_path: Path) -> dict[Path, tuple[str, str]]:
    backups: dict[Path, tuple[str, str]] = {}

    for path in iter_text_files(root, script_path):
        result = read_text(path)
        if result is None:
            continue

        text, encoding = result
        if contains_text_replacement(text):
            backups[path] = (text, encoding)

    return backups


def apply_replacements(backups: dict[Path, tuple[str, str]], new_name: str) -> None:
    for path, (text, encoding) in backups.items():
        path.write_text(replace_text(text, new_name), encoding=encoding, newline="")


def collect_path_renames(
    root: Path,
    script_path: Path,
    new_name: str,
) -> list[tuple[Path, Path]]:
    paths: list[Path] = []

    for dirpath, dirnames, filenames in os.walk(root):
        current_dir = Path(dirpath)
        dirnames[:] = [name for name in dirnames if name not in SKIP_DIRS]

        for dirname in dirnames:
            path = current_dir / dirname
            if NAME_REPLACEMENT_PATTERN.search(dirname):
                paths.append(path)

        for filename in filenames:
            path = current_dir / filename
            if path == script_path:
                continue
            if NAME_REPLACEMENT_PATTERN.search(filename):
                paths.append(path)

    paths.sort(key=lambda path: len(path.relative_to(root).parts), reverse=True)
    return [
        (path, path.with_name(replace_names(path.name, new_name)))
        for path in paths
        if replace_names(path.name, new_name) != path.name
    ]


def validate_path_renames(root: Path, rename_plan: list[tuple[Path, Path]]) -> None:
    target_paths: dict[Path, Path] = {}

    for old_path, new_path in rename_plan:
        previous_source = target_paths.get(new_path)
        if previous_source is not None:
            raise FileExistsError(
                f"多个路径将被重命名为同一目标，已停止避免覆盖: "
                f"{previous_source.relative_to(root)}、{old_path.relative_to(root)} -> "
                f"{new_path.relative_to(root)}"
            )
        target_paths[new_path] = old_path

        if new_path.exists():
            raise FileExistsError(f"目标路径已存在，已停止避免覆盖: {new_path}")


def apply_path_renames(
    rename_plan: list[tuple[Path, Path]],
    completed_renames: list[tuple[Path, Path]],
) -> None:
    for old_path, new_path in rename_plan:
        old_path.rename(new_path)
        completed_renames.append((old_path, new_path))


def copy_to_build_environment(root: Path, script_path: Path, build_root: Path) -> None:
    resolved_root = root.resolve()

    def ignore_files(directory: str, names: list[str]) -> set[str]:
        ignored = {name for name in names if name in SKIP_DIRS}
        if Path(directory).resolve() == resolved_root:
            ignored.add(script_path.name)
        return ignored

    shutil.copytree(root, build_root, ignore=ignore_files, dirs_exist_ok=True)


def get_required_dotnet_sdk(project_path: Path) -> tuple[str, int]:
    result = read_text(project_path)
    if result is None:
        raise RuntimeError(f"无法读取项目文件: {project_path}")

    project_text, _ = result
    match = TARGET_FRAMEWORK_PATTERN.search(project_text)
    if match is None:
        raise RuntimeError("无法从项目文件的 TargetFramework 判断所需 .NET SDK 版本。")

    major = int(match.group("major"))
    minor = int(match.group("minor"))
    return f"{major}.{minor}", major


def get_user_dotnet_paths() -> tuple[Path, Path]:
    local_app_data = os.environ.get("LOCALAPPDATA")
    install_dir = (
        Path(local_app_data) / "Microsoft" / "dotnet"
        if local_app_data
        else Path.home() / ".dotnet"
    )
    return install_dir, install_dir / "dotnet.exe"


def get_installed_sdk_versions(dotnet_command: str | Path) -> list[str]:
    try:
        result = subprocess.run(
            [str(dotnet_command), "--list-sdks"],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    except (OSError, subprocess.CalledProcessError):
        return []

    versions: list[str] = []
    for line in result.stdout.splitlines():
        version = line.partition(" ")[0].strip()
        if version:
            versions.append(version)
    return versions


def find_compatible_dotnet(required_major: int) -> tuple[str | None, list[str]]:
    _, user_dotnet = get_user_dotnet_paths()
    candidates = [shutil.which("dotnet"), str(user_dotnet)]
    checked: set[str] = set()

    for candidate in candidates:
        if not candidate:
            continue

        normalized = os.path.normcase(os.path.abspath(candidate))
        if normalized in checked:
            continue
        checked.add(normalized)

        versions = get_installed_sdk_versions(candidate)
        if any(version.split(".", 1)[0] == str(required_major) for version in versions):
            return candidate, versions

    return None, []


def install_dotnet_sdk(channel: str) -> str:
    install_dir, dotnet_path = get_user_dotnet_paths()
    powershell = shutil.which("powershell") or shutil.which("pwsh")
    if powershell is None:
        raise RuntimeError("找不到 PowerShell，无法自动安装 .NET SDK。")

    install_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="shigure-dotnet-") as temp_dir:
        installer_path = Path(temp_dir) / "dotnet-install.ps1"
        print(f"正在从微软官网下载 .NET {channel} SDK 安装脚本……")
        urllib.request.urlretrieve(DOTNET_INSTALL_SCRIPT_URL, installer_path)

        command = [
            powershell,
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(installer_path),
            "-Channel",
            channel,
            "-Quality",
            "GA",
            "-Architecture",
            "x64",
            "-InstallDir",
            str(install_dir),
            "-NoPath",
        ]
        print(f"正在安装到当前用户目录: {install_dir}")
        subprocess.run(command, check=True)

    if not dotnet_path.exists():
        raise RuntimeError(f"安装完成后仍找不到 dotnet: {dotnet_path}")
    return str(dotnet_path)


def ensure_dotnet_sdk(project_path: Path) -> str:
    channel, required_major = get_required_dotnet_sdk(project_path)
    print()
    print(f"正在检测 .NET {channel} SDK 依赖……")

    dotnet_command, versions = find_compatible_dotnet(required_major)
    if dotnet_command is not None:
        matching_versions = [
            version for version in versions
            if version.split(".", 1)[0] == str(required_major)
        ]
        print(f"已安装兼容的 SDK: {', '.join(matching_versions)}")
        return dotnet_command

    print(f"未检测到 .NET {channel} SDK，开始自动下载和安装。")
    dotnet_command = install_dotnet_sdk(channel)
    versions = get_installed_sdk_versions(dotnet_command)
    if not any(version.split(".", 1)[0] == str(required_major) for version in versions):
        raise RuntimeError(f".NET {channel} SDK 安装后验证失败。")

    print(f".NET {channel} SDK 安装并验证成功。")
    return dotnet_command


def publish(
    build_root: Path,
    new_name: str,
    dotnet_command: str,
    output_dir: Path,
) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    command = [
        dotnet_command,
        "publish",
        f".\\{new_name}.csproj",
        "-c",
        "Release",
        "-r",
        "win-x64",
        "--self-contained",
        "true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-o",
        str(output_dir),
    ]

    print()
    print("开始执行发布命令:")
    print(subprocess.list2cmdline(command))
    subprocess.run(command, cwd=build_root, check=True)


def open_publish_folder(root: Path) -> None:
    publish_dir = root / "artifacts" / "publish" / "win-x64"
    if not publish_dir.exists():
        print(f"打包目录不存在，无法打开: {publish_dir}")
        return

    os.startfile(publish_dir)


def main() -> int:
    configure_console()

    root, script_path = get_app_paths()

    new_name = ask_new_name()
    company_name = ask_company_name()
    should_rename = new_name != OLD_NAME
    source_csproj = root / f"{OLD_NAME}.csproj"
    if not source_csproj.exists():
        print(f"找不到需要打包的项目文件: {source_csproj}")
        return 1

    if should_rename:
        preview_files = list(collect_replacements(root, script_path))
        preview_rename_plan = collect_path_renames(root, script_path, new_name)
        try:
            validate_path_renames(root, preview_rename_plan)
        except (FileExistsError, ValueError) as exc:
            print(exc)
            return 1

        print()
        print(
            f"将把文本和路径中的 {OLD_NAME}、{ADDON_OLD_NAME} 按原文大小写形式 "
            f"替换为 {new_name}，并把 /fu 替换为 /{new_name[:2].lower()}。"
        )
        print(f"将在隔离副本中修改 {len(preview_files)} 个文本文件。")
        for path in preview_files:
            print(f"- {path.relative_to(root)}")
        print(f"将在隔离副本中重命名 {len(preview_rename_plan)} 个文件或目录。")
        for old_path, new_path in preview_rename_plan:
            print(f"- {old_path.relative_to(root)} -> {new_path.relative_to(root)}")
    else:
        print()
        print(f"新名称和原名称相同，将在隔离副本中使用 {OLD_NAME}.csproj 打包。")

    print(f"公司名称将设置为: {company_name}")
    print("所有名称修改仅发生在临时构建环境，不会修改当前项目源码。")

    confirm = input("确认继续？输入 Y/y 继续，其它任意内容取消: ").strip()
    if confirm.casefold() != "y":
        print("已取消。")
        return 0

    try:
        dotnet_command = ensure_dotnet_sdk(source_csproj)
    except BaseException as exc:
        if isinstance(exc, KeyboardInterrupt):
            print("依赖安装已中断，当前项目未被修改。")
        else:
            print(f"依赖检测或安装失败，当前项目未被修改: {exc}")
        return 1

    try:
        with tempfile.TemporaryDirectory(prefix="shigure-build-") as temp_dir:
            build_root = Path(temp_dir) / "project"
            print()
            print(f"正在创建临时构建环境: {build_root}")
            copy_to_build_environment(root, script_path, build_root)

            build_script_path = build_root / script_path.name
            if should_rename:
                build_backups = collect_replacements(build_root, build_script_path)
                build_rename_plan = collect_path_renames(
                    build_root,
                    build_script_path,
                    new_name,
                )
                validate_path_renames(build_root, build_rename_plan)

                completed_renames: list[tuple[Path, Path]] = []
                apply_replacements(build_backups, new_name)
                apply_path_renames(build_rename_plan, completed_renames)

                print(f"隔离副本中已修改 {len(build_backups)} 个文本文件。")
                print(f"隔离副本中已重命名 {len(completed_renames)} 个文件或目录。")

            project_path = build_root / f"{new_name}.csproj"
            update_company_name(project_path, company_name)
            print(f"隔离副本中的公司名称已设置为: {company_name}")

            output_dir = root / "artifacts" / "publish" / "win-x64"
            publish(build_root, new_name, dotnet_command, output_dir)
    except BaseException as exc:
        if isinstance(exc, KeyboardInterrupt):
            print("执行已中断。")
        else:
            print(f"执行失败: {exc}")
        print("临时构建环境已清理，当前项目源码未被修改。")
        return 1

    print()
    print("打包完成，临时构建环境已自动清理，当前项目源码未被修改。")
    open_publish_folder(root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
