"""构建包含 Retail / Forever 的离线技能、物品目录（仅使用 Python 标准库）。"""
import argparse
import concurrent.futures
import csv
import hashlib
import http.client
import io
import json
import os
from pathlib import Path
import re
import struct
import threading
import time
import urllib.request
import urllib.parse
import zipfile
from datetime import datetime, timezone

_path_locks = {}
_path_locks_guard = threading.Lock()
_connections = threading.local()
_offline = False


def read_url(url):
    # 图标 CDN 支持持久连接，避免数千次重复 TLS 握手；代理环境保留 urllib 的代理处理。
    parsed = urllib.parse.urlsplit(url)
    if parsed.hostname == "wow.zamimg.com" and not urllib.request.getproxies().get("https"):
        connection = getattr(_connections, "cdn", None)
        if connection is None:
            connection = http.client.HTTPSConnection(parsed.hostname, timeout=40)
            _connections.cdn = connection
        try:
            connection.request("GET", parsed.path, headers={"User-Agent": "Shigure-DataBuilder/2.0"})
            response = connection.getresponse()
            data = response.read()
            if response.status != 200:
                raise ValueError(f"图标 CDN 返回 HTTP {response.status}: {url}")
            return data
        except Exception:
            connection.close()
            _connections.cdn = None
            raise
    request = urllib.request.Request(url, headers={"User-Agent": "Shigure-DataBuilder/2.0"})
    with urllib.request.urlopen(request, timeout=40) as response:
        return response.read()


def sha(data):
    return hashlib.sha256(data).hexdigest()


def fetch(url, path, validate=None):
    # 不同 FileDataID 可能引用同一图标名；同一个缓存文件只允许一个下载事务。
    with _path_locks_guard:
        lock = _path_locks.setdefault(path, threading.Lock())
    with lock:
        return fetch_locked(url, path, validate)


def fetch_locked(url, path, validate=None):
    if path.exists():
        data = path.read_bytes()
        if validate is None or validate(data):
            return data
    if _offline:
        raise FileNotFoundError("离线缓存未命中: " + url)
    failure = None
    for attempt in range(3):
        try:
            data = read_url(url)
            if validate and not validate(data):
                raise ValueError("响应格式不正确: " + url)
            path.parent.mkdir(parents=True, exist_ok=True)
            temporary = path.with_suffix(path.suffix + ".download")
            temporary.write_bytes(data)
            temporary.replace(path)
            return data
        except Exception as error:
            failure = error
            time.sleep(attempt + 1)
    raise RuntimeError(str(failure))


def import_retail(path):
    """原样保留旧包的 ID / 名称 / 图标关系，不推断旧包来源或构建号。"""
    stream = path.open("rb")
    header = stream.read(56)
    magic, version, spell_count, icon_count, name_count, spell_offset, icon_offset, name_offset, data_offset = struct.unpack("<8siiiiqqqq", header)
    if magic != b"SHGICN1\0" or version != 1:
        stream.close()
        raise ValueError("Retail 输入必须是现有 v1 数据包；请保留原包用于重复构建")

    def maps(offset, count):
        stream.seek(offset)
        return dict(struct.unpack("<qi", stream.read(12)) for _ in range(count))

    def names(offset, count):
        stream.seek(offset)
        result = {}
        for _ in range(count):
            entry_id, length = struct.unpack("<qi", stream.read(12))
            result.setdefault(entry_id, stream.read(length).decode("utf-8"))
        return result

    spell_map = maps(spell_offset, spell_count)
    spell_names = names(name_offset, name_count)
    stream.seek(icon_offset)
    locations = [struct.unpack("<qi", stream.read(12)) for _ in range(icon_count)]
    stream.seek(-48, 2)
    footer = stream.read(48)
    item_map, item_names = {}, {}
    if footer[:8] == b"SHGITM1\0":
        _, item_count, item_name_count, item_offset, item_name_offset, _, _ = struct.unpack("<8siiqqqq", footer)
        item_map = maps(item_offset, item_count)
        item_names = names(item_name_offset, item_name_count)
    with path.open("rb") as package_stream:
        digest = hashlib.file_digest(package_stream, "sha256").hexdigest()
    info = {"version": "Retail", "build": None, "locale": "imported", "sources": [{"kind": "legacy-package", "sha256": digest}]}
    return stream, locations, spell_map, spell_names, item_map, item_names, info


def build(args):
    global _offline
    _offline = args.offline
    if not re.fullmatch(r"1\.60\.\d+\.\d+", args.forever_build):
        raise ValueError("Forever 构建号必须属于 1.60 系列")
    cache = args.cache.resolve()
    source_dir = cache / args.forever_build
    sources = []

    def table(name, locale):
        url = f"https://wago.tools/db2/{name}/csv?build={args.forever_build}&locale={locale}"
        data = fetch(url, source_dir / f"{name}-{locale}.csv", lambda b: b.lstrip(b"\xef\xbb\xbf").startswith(b"ID,"))
        rows = list(csv.DictReader(io.StringIO(data.decode("utf-8-sig"))))
        if not rows or len({int(r["ID"]) for r in rows}) != len(rows):
            raise ValueError("源表为空或 ID 重复: " + name)
        sources.append({"url": url, "sha256": sha(data), "rows": len(rows)})
        print(f"源表 {name} / {locale}: {len(rows)}", flush=True)
        return rows

    # 每张表固定同一个构建，中文和英文也不跨构建借用记录。
    spell_zh = table("SpellName", "zhCN")
    spell_en = {int(r["ID"]): r["Name_lang"] for r in table("SpellName", "enUS")}
    misc = table("SpellMisc", "enUS")
    item_zh = table("ItemSparse", "zhCN")
    item_en = {int(r["ID"]): r["Display_lang"] for r in table("ItemSparse", "enUS")}
    items = table("Item", "enUS")
    item_icons = {int(r["ID"]): int(r["IconFileDataID"]) for r in items}
    spell_icons = {}
    for row in sorted(misc, key=lambda r: (int(r["DifficultyID"]), int(r["ID"]))):
        spell_icons.setdefault(int(row["SpellID"]), int(row["SpellIconFileDataID"]))
    spells = [{"id": int(r["ID"]), "name": r["Name_lang"].strip() or spell_en.get(int(r["ID"]), "").strip(), "fileId": spell_icons.get(int(r["ID"]), 0)} for r in spell_zh]
    item_records = [{"id": int(r["ID"]), "name": r["Display_lang"].strip() or item_en.get(int(r["ID"]), "").strip(), "fileId": item_icons.get(int(r["ID"]), 0)} for r in item_zh]
    for kind, records in (("技能", spells), ("物品", item_records)):
        for record in records:
            record["name"] = record["name"] or f"{kind} {record['id']}"
        records.sort(key=lambda r: r["id"])

    # 文件名映射由维护该映射的 wowdev 项目发布；保存内容摘要以便复现。
    list_url = "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv"
    list_data = fetch(list_url, cache / "community-listfile.csv", lambda b: b";" in b[:200])
    sources.append({"url": list_url, "sha256": sha(list_data)})
    needed = {r["fileId"] for r in spells + item_records if r["fileId"] > 0}
    icon_names = {}
    for line in list_data.decode("utf-8-sig").splitlines():
        number, _, filename = line.partition(";")
        if number.isdigit() and int(number) in needed and filename.lower().startswith("interface/icons/"):
            icon_names[int(number)] = Path(filename).stem.lower()
    failures = {}
    downloaded = {}
    groups = {}
    for kind, records in (("spell", spells), ("item", item_records)):
        for record in records:
            key = ("file", record["fileId"]) if record["fileId"] else (kind, record["id"])
            groups.setdefault(key, (kind, record["id"], record["fileId"]))

    def load_icon(slug):
        slug = (slug or "").strip().lower()
        if not re.fullmatch(r"[a-z0-9_.& -]+", slug):
            raise ValueError("Forever tooltip 未提供有效的图标名")
        url = "https://wow.zamimg.com/images/wow/icons/large/" + urllib.parse.quote(slug, safe="") + ".jpg"
        path = cache / "icons" / (slug + ".jpg")
        data = fetch(url, path, lambda b: len(b) >= 512 and b.startswith(b"\xff\xd8") and b.endswith(b"\xff\xd9"))
        return path, sha(data)

    def icon_job(pair):
        key, (kind, entry_id, file_id) = pair
        mapping_failure = None
        if icon_names.get(file_id):
            try:
                path, digest = load_icon(icon_names[file_id])
                return key, path, digest, None
            except Exception as error:
                mapping_failure = str(error)
        try:
            # 文件名映射有时包含过期别名；以 Forever 自己的 tooltip 补充，不猜测其他版本。
            tip_url = f"https://nether.wowhead.com/tooltip/{kind}/{entry_id}?dataEnv=16&locale=4"
            tip = json.loads(fetch(tip_url, source_dir / "tooltips" / f"{kind}-{entry_id}.json", lambda b: b.startswith(b"{")))
            path, digest = load_icon(tip.get("icon"))
            return key, path, digest, None
        except Exception as error:
            return key, None, None, (mapping_failure + "；" if mapping_failure else "") + str(error)

    print(f"获取 {len(groups)} 个独立图标，使用 {args.workers} 个下载线程", flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        for count, (key, path, digest, failure) in enumerate(pool.map(icon_job, groups.items()), 1):
            if failure:
                failures[str(key)] = failure
            else:
                downloaded[key] = (path, digest)
            if count % 100 == 0 or count == len(groups):
                print(f"图标 {count}/{len(groups)}，缺失 {len(failures)}", flush=True)

    for kind, records in (("spell", spells), ("item", item_records)):
        for record in records:
            key = ("file", record.pop("fileId"))
            if key[1] == 0:
                key = (kind, record["id"])
            record["icon"] = downloaded.get(key, (None, None))[1]

    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(".shgpack.download")
    legacy_stream, locations, retail_spells, retail_spell_names, retail_items, retail_item_names, retail_info = import_retail(args.retail_package)
    seen = set()
    retail_icons = []
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
            for offset, length in locations:
                legacy_stream.seek(offset)
                data = legacy_stream.read(length)
                if len(data) != length:
                    raise ValueError("Retail 图标截断")
                digest = sha(data)
                retail_icons.append(digest)
                if digest not in seen:
                    archive.writestr(f"icons/{digest}.jpg", data)
                    seen.add(digest)
            for path, digest in downloaded.values():
                if digest not in seen:
                    archive.write(path, f"icons/{digest}.jpg")
                    seen.add(digest)
            retail_spell_records = [{"id": entry_id, "name": retail_spell_names.get(entry_id, ""), "icon": retail_icons[index]} for entry_id, index in sorted(retail_spells.items())]
            retail_item_records = [{"id": entry_id, "name": retail_item_names.get(entry_id, ""), "icon": retail_icons[index]} for entry_id, index in sorted(retail_items.items())]
            catalogs = []
            for info, spell_records, item_rows in ((retail_info, retail_spell_records, retail_item_records), ({"version": "Forever", "build": args.forever_build, "locale": "zhCN/enUS", "sources": sources}, spells, item_records)):
                version = info["version"]
                info.update({"spellCount": len(spell_records), "itemCount": len(item_rows), "spells": f"catalogs/{version}/spells.json", "items": f"catalogs/{version}/items.json"})
                archive.writestr(info["spells"], json.dumps(spell_records, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
                archive.writestr(info["items"], json.dumps(item_rows, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
                catalogs.append(info)
            manifest = {"formatVersion": 2, "createdUtc": datetime.now(timezone.utc).isoformat(), "iconCount": len(seen), "catalogs": catalogs}
            archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False).encode("utf-8"))
        os.replace(temporary, args.output)
    finally:
        legacy_stream.close()
    with args.output.open("rb") as package_stream:
        package_hash = hashlib.file_digest(package_stream, "sha256").hexdigest()
    report = {**manifest, "package": str(args.output.resolve()), "packageBytes": args.output.stat().st_size, "packageSha256": package_hash, "missingIcons": failures, "foreverMissingSpellIconIds": [r["id"] for r in spells if not r["icon"]], "foreverMissingItemIconIds": [r["id"] for r in item_records if not r["icon"]], "coverage": {"SpellName": len(spells), "ItemSparse": len(item_records)}}
    args.output.with_suffix(".report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# Shigure 两版数据包来源报告", "", f"生成时间（UTC）：{manifest['createdUtc']}", f"包格式：v2；大小：{report['packageBytes'] / 1024 / 1024:.2f} MiB", f"包 SHA-256：`{package_hash}`", "", "| 目录 | 构建 | 技能 | 物品 |", "| --- | --- | ---: | ---: |"]
    for catalog in catalogs:
        lines.append(f"| {catalog['version']} | {catalog['build'] or '未知（原包完整导入）'} | {catalog['spellCount']:,} | {catalog['itemCount']:,} |")
    lines += ["", f"Forever 技能 ID 覆盖完整 SpellName 表（{len(spells):,} 条），物品 ID 覆盖完整 ItemSparse 表（{len(item_records):,} 条）。", f"共享图标内容：{len(seen):,} 份。未获取到图标的技能 {len(report['foreverMissingSpellIconIds']):,} 条，物品 {len(report['foreverMissingItemIconIds']):,} 条；名称和 ID 仍完整保留。未公开的图标、失效链接或离线缓存缺失等具体原因见同名 JSON 报告。", "", "## 数据来源", ""]
    for source in sources:
        lines.append(f"- [{source['url']}]({source['url']})；SHA-256 `{source['sha256']}`" + (f"；{source['rows']:,} 条记录" if 'rows' in source else ""))
    lines += ["", "图标文件名来自 wowdev 的文件名映射；缺少映射时读取 Wowhead Forever tooltip（`dataEnv=16&locale=4`），图标内容来自 `wow.zamimg.com/images/wow/icons/large/`。图标按字节内容 SHA-256 去重，不按技能 ID、物品 ID 或文件名跨版本替换。", "", "Retail 没有可验证的客户端构建信息，保留为未知；原始 v1 包的 SHA-256 已记录在 JSON 来源字段。"]
    args.output.with_suffix(".report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"完成: {args.output}，SHA-256 {package_hash}；共享图标 {len(seen)}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--retail-package", type=Path, required=True)
    parser.add_argument("--forever-build", default="1.60.1.70235")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, default=Path("artifacts/game-data/sources"))
    parser.add_argument("--workers", type=int, default=6, choices=range(1, 13))
    parser.add_argument("--offline", action="store_true", help="只用既有缓存重建；缺少源表时失败，缺少图标时保留占位记录")
    build(parser.parse_args())
