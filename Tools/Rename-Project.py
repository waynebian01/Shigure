"""区分大小写地重命名本项目；默认预览，使用 --apply 执行。

用法：python Tools/Rename-Project.py [--root 项目目录] [--apply]
只处理仓库文件，不迁移用户数据、部署游戏插件或修改仓库根目录名。
"""

import argparse
import codecs
from dataclasses import dataclass
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import zipfile


SKIP_DIRECTORIES = {
    ".git", ".vs", ".vscode", ".claude", ".codex", ".agents", ".cursor",
    ".venv", "venv", "node_modules", "__pycache__", "bin", "obj", "artifacts",
    "cache", "module", "data", "outputs", "spelliconpackage",
    "libs", "vendor", "third_party", "third-party",
}
TEXT_SUFFIXES = {
    ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".config",
    ".manifest", ".resx", ".xml", ".json", ".lua", ".toc", ".md", ".txt",
    ".ps1", ".psm1", ".py", ".cmd", ".bat", ".sh", ".yml", ".yaml",
    ".toml", ".ini", ".editorconfig", ".svg", ".html", ".css", ".js",
}
TEXT_FILENAMES = {"LICENSE", "NOTICE", ".gitignore", ".gitattributes", ".editorconfig"}
SKIP_FILENAMES = {"提示词帮助.md", "spell_id_bytes.py"}
URL_PATTERN = re.compile(r"(?:https?|ftp)://[^\s<>\"'`]+")
COMMAND_PATTERN = re.compile(r"(?<![\w/])/(fuyutsui|no)(?![\w/-])")
SLASH_IDENTIFIER_PATTERN = re.compile(r"\bSLASH_NOWAKI([12])\b")
SLASH_KEY_PATTERN = re.compile(r"(\bSlashCmdList\s*\[\s*)([\"'])NOWAKI\2(\s*\])")


@dataclass(frozen=True)
class Edit:
    path: Path
    original: bytes
    updated: bytes


@dataclass(frozen=True)
class Rename:
    source: Path
    target: Path


@dataclass(frozen=True)
class Plan:
    root: Path
    edits: tuple[Edit, ...]
    renames: tuple[Rename, ...]


def rename_name(name: str) -> str:
    return name.replace("Shigure", "Shigure").replace("Fuyutsui", "Fuyutsui")


def replace_fragment(text: str) -> str:
    text = rename_name(text)
    text = SLASH_IDENTIFIER_PATTERN.sub(r"SLASH_FUYUTSUI\1", text)
    text = SLASH_KEY_PATTERN.sub(lambda m: m[1] + m[2] + "FUYUTSUI" + m[2] + m[3], text)
    return COMMAND_PATTERN.sub(lambda m: "/fuyutsui" if m[1] == "fuyutsui" else "/fu", text)


def replace_text(text: str) -> str:
    # URL 整段原样保留，避免重命名不存在的网站或下载地址。
    parts = []
    start = 0
    for match in URL_PATTERN.finditer(text):
        parts.extend((replace_fragment(text[start:match.start()]), match[0]))
        start = match.end()
    parts.append(replace_fragment(text[start:]))
    return "".join(parts)


def transform_bytes(data: bytes) -> bytes:
    # 长 BOM 必须先判断；直接处理字节，避免文本模式改变换行符。
    formats = (
        (codecs.BOM_UTF32_LE, "utf-32-le"), (codecs.BOM_UTF32_BE, "utf-32-be"),
        (codecs.BOM_UTF8, "utf-8"),
        (codecs.BOM_UTF16_LE, "utf-16-le"), (codecs.BOM_UTF16_BE, "utf-16-be"),
    )
    for bom, encoding in formats:
        if data.startswith(bom):
            return bom + replace_text(data[len(bom):].decode(encoding)).encode(encoding)
    if b"\x00" in data:
        raise ValueError("文本文件包含空字节，无法确定编码")
    # 无 BOM 优先采用仓库使用的 UTF-8，兼容 Windows 简体中文 ANSI 文件。
    try:
        text = data.decode("utf-8")
        encoding = "utf-8"
    except UnicodeDecodeError:
        text = data.decode("gb18030")
        encoding = "gb18030"
    return replace_text(text).encode(encoding)


def skip_file(path: Path, script: Path) -> bool:
    name = path.name.lower()
    return (
        path.resolve() == script
        or path.name == "Rename-Project.py"
        or path.name in SKIP_FILENAMES
        or name.startswith(".env")
        or any(word in name for word in ("credential", "secret", "token", "password"))
        or name.endswith((".user", ".suo", ".userosscache", ".sln.docstates"))
        or path.name in {".prompt", ".mean", ".plan"}
    )


def create_plan(root: Path) -> Plan:
    if not any((root / name).is_file() for name in ("Shigure.csproj", "Shigure.csproj")):
        raise ValueError(f"项目目录无效，未找到原名或新名项目文件：{root}")
    if not (root / "game_profiles.json").is_file() or not (root / "Retail").is_dir():
        raise ValueError(f"项目目录缺少 game_profiles.json 或 Retail：{root}")

    script = Path(__file__).resolve()
    edits = []
    renames = []

    def walk_error(error: OSError) -> None:
        raise error

    for directory, dirs, files in os.walk(root, followlinks=False, onerror=walk_error):
        parent = Path(directory)
        # 不进入链接或 Windows 目录联接，所有处理都限制在实际仓库树内。
        dirs[:] = sorted(name for name in dirs
                         if name.lower() not in SKIP_DIRECTORIES
                         and not (parent / name).is_symlink()
                         and not (parent / name).is_junction())
        for name in dirs:
            if rename_name(name) != name:
                renames.append(Rename(parent / name, parent / rename_name(name)))
        for name in sorted(files):
            path = parent / name
            if path.is_symlink() or skip_file(path, script):
                continue
            if rename_name(name) != name:
                renames.append(Rename(path, path.with_name(rename_name(name))))
            if path.suffix.lower() not in TEXT_SUFFIXES and name not in TEXT_FILENAMES:
                continue
            try:
                original = path.read_bytes()
                updated = transform_bytes(original)
            except (OSError, UnicodeError, ValueError) as error:
                raise ValueError(f"读取或解码失败：{path}：{error}") from error
            if original != updated:
                if not path.stat().st_mode & stat.S_IWRITE:
                    raise ValueError(f"文件为只读，请先解除只读：{path}")
                edits.append(Edit(path, original, updated))

    # 先移动子项，再移动父目录；每个目标均为当前父目录内的新名称。
    renames.sort(key=lambda item: (-len(item.source.parts), str(item.source)))
    targets = set()
    for rename in renames:
        target_key = os.path.normcase(str(rename.target))
        if os.path.lexists(rename.target) or target_key in targets:
            raise ValueError(f"重命名目标冲突，未修改任何文件：{rename.source} → {rename.target}")
        targets.add(target_key)
    return Plan(root, tuple(edits), tuple(renames))


def show_plan(plan: Plan) -> None:
    print(f"项目目录：{plan.root}")
    for edit in plan.edits:
        print(f"修改内容：{edit.path.relative_to(plan.root)}")
    for rename in plan.renames:
        print(f"重命名：{rename.source.relative_to(plan.root)} → {rename.target.relative_to(plan.root)}")
    print(f"共 {len(plan.edits)} 个内容修改、{len(plan.renames)} 个路径重命名。")


def backup_plan(plan: Plan) -> Path:
    backup_dir = plan.root / "artifacts" / "rename-project"
    if backup_dir.is_symlink() or not backup_dir.resolve().is_relative_to(plan.root):
        raise ValueError(f"备份目录必须位于仓库内部：{backup_dir}")
    backup_dir.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    backup = backup_dir / f"backup-{timestamp}.zip"
    originals = {edit.path: edit.original for edit in plan.edits}
    for rename in plan.renames:
        if rename.source.is_file() and rename.source not in originals:
            originals[rename.source] = rename.source.read_bytes()
    manifest = {
        "root": str(plan.root),
        "createdUtc": timestamp,
        "edits": [str(edit.path.relative_to(plan.root)) for edit in plan.edits],
        "renames": [{"source": str(item.source.relative_to(plan.root)),
                     "target": str(item.target.relative_to(plan.root))} for item in plan.renames],
        "recovery": "先将已完成的重命名按执行顺序逆序恢复，再把 originals/ 中的文件覆盖回原路径。",
    }
    with zipfile.ZipFile(backup, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
        for path, data in originals.items():
            archive.writestr("originals/" + path.relative_to(plan.root).as_posix(), data)
    return backup


def write_atomic(edit: Edit) -> None:
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=edit.path.parent, prefix=".rename-", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(edit.updated)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, stat.S_IMODE(edit.path.stat().st_mode))
        os.replace(temporary, edit.path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def apply_plan(plan: Plan) -> None:
    # 防止预览计划生成后，其他编辑器保存的内容被旧快照覆盖。
    for edit in plan.edits:
        if edit.path.read_bytes() != edit.original:
            raise ValueError(f"文件在检查后发生变化，请重新执行：{edit.path}")
    backup = backup_plan(plan)
    print(f"备份：{backup}", flush=True)
    completed_edits = 0
    completed_renames = 0
    current = ""
    try:
        for edit in plan.edits:
            current = str(edit.path)
            if edit.path.read_bytes() != edit.original:
                raise ValueError("文件在检查后发生变化")
            write_atomic(edit)
            completed_edits += 1
        for rename in plan.renames:
            current = f"{rename.source} → {rename.target}"
            if os.path.lexists(rename.target):
                raise ValueError("目标路径已存在")
            rename.source.rename(rename.target)
            completed_renames += 1
    except Exception as error:
        raise RuntimeError(
            f"执行失败：{current}：{error}\n"
            f"已完成 {completed_edits} 个内容修改、前 {completed_renames} 个路径重命名；"
            f"可按备份 manifest.json 的顺序恢复。备份：{backup}"
        ) from error
    print("重命名完成。用户数据和游戏目录未迁移。")


def main() -> int:
    # Windows 管道默认可能采用 ANSI；统一输出 UTF-8，避免中文日志乱码。
    for stream in (sys.stdout, sys.stderr):
        if not stream.isatty():
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent,
                        help="项目根目录，默认取脚本所在项目")
    parser.add_argument("--apply", action="store_true", help="备份并执行；不指定时仅预览")
    args = parser.parse_args()
    try:
        plan = create_plan(args.root.resolve())
        show_plan(plan)
        if not plan.edits and not plan.renames:
            print("无需修改。")
        elif args.apply:
            apply_plan(plan)
        else:
            print("当前仅预览，未写入文件。添加 --apply 执行。")
        return 0
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile) as error:
        print(f"错误：{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
