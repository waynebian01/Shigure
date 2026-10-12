"""从 Wowhead Forever 职业技能页获取等级数据；原始页面缓存到 artifacts。"""
import concurrent.futures
import datetime
import json
import html
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request
import urllib.error
import time

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / "artifacts" / "forever-spells"
CLASSES = ["Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Shaman", "Mage", "Warlock", "Druid"]


def fetch(url, filename):
    path = CACHE / filename
    if not path.exists():
        request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        for attempt in range(3):
            try:
                with urllib.request.urlopen(request, timeout=25) as response:
                    text = response.read().decode("utf-8")
                break
            except urllib.error.HTTPError as error:
                if error.code == 403:
                    shell = shutil.which("pwsh") or shutil.which("powershell")
                    if shell:
                        path.parent.mkdir(parents=True, exist_ok=True)
                        result = subprocess.run([shell, "-NoProfile", "-File", str(ROOT / "scripts/fetch_wowhead.ps1"),
                                                 "-Url", url, "-Destination", str(path)], capture_output=True, timeout=60,
                                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                        if result.returncode == 0:
                            return path.read_text(encoding="utf-8-sig")
                if error.code == 404 or attempt == 2:
                    raise
                time.sleep(2)
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(2)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return path.read_text(encoding="utf-8")


def parse_rows(page):
    match = re.search(r"var listviewspells = (\[.*?\]);", page)
    if not match:
        raise ValueError("未找到技能数据")
    raw = re.sub(r",(?:quality|popularity):-?\d+", "", match[1])
    return json.loads(raw)


def sort_spell_entries(table):
    """按名称、等级、法术 ID 排列，保留每条原文及其注释。"""
    entries = []
    comments = []
    for line in table.splitlines():
        if not line.strip():
            continue
        if line.lstrip().startswith("--"):
            comments.append(line)
            continue
        spell_id = re.match(r"\s*\[(\d+)\]\s*=", line)
        name = re.search(r'name\s*=\s*("(?:\\.|[^"\\])*")', line)
        rank = re.search(r"\bRank\s*=\s*(\d+)", line)
        if not (spell_id and name and rank):
            raise ValueError("无法识别技能条目，拒绝排序: " + line)
        key = (json.loads(name[1]), int(rank[1]), int(spell_id[1]))
        entries.append((key, comments + [line]))
        comments = []
    lines = [line for _, block in sorted(entries) for line in block] + comments
    return "\n" + "\n".join(lines)


def read_class(name):
    url = f"https://www.wowhead.com/forever/cn/spells/abilities/{name.lower()}?filter=50;2;0"
    page = fetch(url, name + ".html")
    rows = parse_rows(page)
    talent_url = url.replace("/abilities/", "/talents/")
    talents = parse_rows(fetch(talent_url, name + ".talents.active.html"))
    merged = {row["id"]: {**row, "sourceCategory": "abilities"} for row in rows}
    for row in talents:
        merged[row["id"]] = {**row, "sourceCategory": "talents"}
    rows = list(merged.values())
    return name, url, rows


def generate(data, flags):
    print("详情核对完成，开始校验中英文列表和等级分段完整性。", flush=True)
    report = {"source": "Wowhead Forever", "date": datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).date().isoformat(),
              "scope": "九职业可施放技能及全部等级；排除被动、宠物独立技能、已移除技能和内部触发效果；现有自定义条目保留", "classes": []}
    staged_files = []
    # 完整性分段只读取源数据，与详情核对独立；批量请求避免逐职业串行等待。
    partitions = [(url.replace("/abilities/", "/" + category + "/").replace("?", "/" + facet + "?"),
                   name + "." + category + "." + facet.replace(":", "-") + ".html")
                  for name, url, _ in data for category in ["abilities", "talents"] for facet in ["max-level:30", "min-level:31"]]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        list(pool.map(lambda part: fetch(*part), partitions))
    for name, url, rows in data:
        en_page = fetch(url.replace("/cn/", "/"), name + ".en.html")
        english = {row["id"]: row for row in parse_rows(en_page)}
        english.update({row["id"]: row for row in parse_rows(fetch(url.replace("/cn/", "/").replace("/abilities/", "/talents/"), name + ".talents.en.html"))})
        if set(english) != {row["id"] for row in rows}:
            raise ValueError(f"{name}: 中英文列表不一致")
        # 按等级分段取完整列表，确认页面返回的数据未截断。
        partition_ids = set()
        for category in ["abilities", "talents"]:
            for facet in ["max-level:30", "min-level:31"]:
                partition_url = url.replace("/abilities/", "/" + category + "/").replace("?", "/" + facet + "?")
                partition_ids.update(row["id"] for row in parse_rows(fetch(partition_url, name + "." + category + "." + facet.replace(":", "-") + ".html")))
        if partition_ids != {row["id"] for row in rows}:
            raise ValueError(f"{name}: 等级分段列表与完整页面不同，拒绝生成不完整技能表")
        groups, excluded = {}, []
        for row in rows:
            reason = None
            text = flags.get(str(row["id"]), "")
            if row.get("envChange", {}).get("status") == "removed":
                reason = "Wowhead 标记已移除"
            elif row["name"].startswith("铭刻"):
                reason = "符文铭刻配方"
            elif name == "Hunter" and row.get("skill") == []:
                reason = "宠物训练或宠物独立技能（不属于玩家职业技能分类）"
            elif "未核实" in text:
                raise ValueError(f"{name}/{row['id']}: {text}")
            elif "Tradeskill recipe" in text:
                reason = "学习配方或宠物训练条目"
            elif any(flag in text for flag in ["Not an Action", "No Cast Log", "Do Not Display Cast Time", "Not In Spellbook"]):
                reason = "内部触发效果: " + text
            if reason:
                excluded.append({"spellId": row["id"], "name": row["name"], "reason": reason})
                continue
            row = dict(row)
            row["family"] = english[row["id"]]["name"]
            match = re.search(r"\d+", row.get("rank", ""))
            row["Rank"] = int(match[0]) if match else 1
            groups.setdefault(row["family"], []).append(row)
        path = ROOT / "Forever" / "Shingen" / "class" / (name + ".lua")
        source = path.read_text(encoding="utf-8")
        table_match = re.search(r"Shingen\.spellsList\s*=\s*\{(.*?)\n\}", source, re.S)
        if not table_match:
            raise ValueError(f"{name}: 找不到 spellsList")
        entry_pattern = re.compile(r'^([ \t]*\[\s*(\d+)\s*\]\s*=\s*\{\s*index\s*=\s*)(\d+)(\s*,\s*name\s*=\s*)("(?:\\.|[^"\\])*")(.*)$', re.M)
        originals = {}
        for match in entry_pattern.finditer(table_match[1]):
            rank_match = re.search(r"Rank\s*=\s*(\d+)", match[6])
            originals[int(match[2])] = {"index": int(match[3]), "name": json.loads(match[5]), "Rank": int(rank_match[1]) if rank_match else None}
        # 既有自定义条目只有同名唯一对应一个技能组时才归入该组。
        id_to_family = {row["id"]: family for family, spells in groups.items() for row in spells}
        for spell_id, old in originals.items():
            if spell_id in id_to_family:
                continue
            candidates = [family for family, spells in groups.items() if any(row["name"] == old["name"] for row in spells)]
            family = candidates[0] if len(candidates) == 1 else f"custom:{spell_id}"
            groups.setdefault(family, []).append({"id": spell_id, "name": old["name"], "Rank": old["Rank"] or 1, "custom": True})
        assignments = {}
        used = {old["index"] for old in originals.values()}
        for family, spells in sorted(groups.items(), key=lambda item: min(row["id"] for row in item[1])):
            anchors = sorted([row for row in spells if row["id"] in originals], key=lambda row: (row["Rank"], row["id"]))
            index = originals[anchors[0]["id"]]["index"] if anchors else next(i for i in range(1, 256) if i not in used)
            used.add(index)
            display_name = originals[anchors[0]["id"]]["name"] if anchors else min(spells, key=lambda row: (row["Rank"], row["id"]))["name"]
            for row in spells:
                assignments[row["id"]] = {"index": index, "name": display_name, "Rank": row["Rank"], "family": family, "custom": row.get("custom", False)}
        def rewrite(match):
            spell_id = int(match[2]); row = assignments[spell_id]
            before, after = match[6].split("}", 1)
            before = re.sub(r",\s*Rank\s*=\s*\d+", "", before).rstrip().rstrip(",")
            suffix = before + f", Rank = {row['Rank']} }}" + after
            return match[1] + str(row["index"]) + match[4] + json.dumps(row["name"], ensure_ascii=False) + suffix
        table = entry_pattern.sub(rewrite, table_match[1])
        new_rows = sorted(((spell_id, row) for spell_id, row in assignments.items() if spell_id not in originals), key=lambda item: (item[1]["index"], item[1]["Rank"], item[0]))
        for spell_id, row in new_rows:
            table += f'\n    [{spell_id}] = {{ index = {row["index"]}, name = {json.dumps(row["name"], ensure_ascii=False)}, Rank = {row["Rank"]} }},'
        table = sort_spell_entries(table)
        source = source[:table_match.start(1)] + table + source[table_match.end(1):]
        def add_state(match):
            fields = match[1]
            for state in ["施法技能", "技能等级"]:
                if '"' + state + '"' not in fields: fields = fields.rstrip() + ', "' + state + '" '
            return '["状态"] = {' + fields + '}'
        source = re.sub(r'\["状态"\]\s*=\s*\{([^}]*)\}', add_state, source, count=1)
        staged_files.append((path, source))
        report["classes"].append({"class": name, "url": url, "talentUrl": url.replace("/abilities/", "/talents/"), "sourceCount": len(rows), "includedCount": sum(not row["custom"] for row in assignments.values()),
                                  "fetchedAt": datetime.datetime.fromtimestamp((CACHE / (name + ".html")).stat().st_mtime, datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
                                  "talentsFetchedAt": datetime.datetime.fromtimestamp((CACHE / (name + ".talents.active.html")).stat().st_mtime, datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
                                  "customCount": sum(row["custom"] for row in assignments.values()), "familyCount": len(groups), "maxIndex": max(row["index"] for row in assignments.values()),
                                  "completeness": "中英文 spellId 集合一致，0–30 与 31+ 等级分段并集一致", "excluded": excluded,
                                  "spells": [{"spellId": spell_id, **row} for spell_id, row in sorted(assignments.items())]})
        print(f"{name}: 收录 {len(assignments)} 条、{len(groups)} 组", flush=True)
    staged_files.append((ROOT / "Forever" / "reference" / "职业技能等级来源.json", json.dumps(report, ensure_ascii=False, indent=2) + "\n"))
    # 完成全部职业的完整性检查后再提交，避免网络失败产生半套职业表。
    for path, text in staged_files:
        temp = path.with_suffix(path.suffix + ".tmp")
        temp.write_text(text, encoding="utf-8", newline="\r\n")
        temp.replace(path)


def spell_flags(spell_id):
    try:
        try:
            page = fetch(f"https://www.wowhead.com/forever/spell={spell_id}", f"{spell_id}.html")
        except urllib.error.HTTPError:
            # 部分 ID 的简写地址不可用，使用官方 tooltip 提供的规范详情链接。
            tooltip = json.loads(fetch(f"https://nether.wowhead.com/forever/tooltip/spell/{spell_id}", f"{spell_id}.tooltip.json"))
            href = re.search(r'href="(/forever/spell=[^"]+)"', tooltip["tooltip"])[1]
            page = fetch("https://www.wowhead.com" + html.unescape(href), f"{spell_id}.html")
    except Exception as error:
        return spell_id, "未核实: " + str(error)
    match = re.search(r'<th[^>]*>\s*Flags\s*</th>\s*<td[^>]*>(.*?)</td>', page, re.S)
    flags = html.unescape(re.sub(r"<[^>]+>", " ", match[1])) if match else ""
    return spell_id, flags


if __name__ == "__main__":
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        data = list(pool.map(read_class, CLASSES))
    for name, url, rows in data:
        print(name, len(rows), len({s["name"] for s in rows}))
        (CACHE / (name + ".json")).write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    ids = sorted({row["id"] for class_name, _, rows in data for row in rows
                  if not row.get("source") and row.get("envChange", {}).get("status") != "removed"
                  and not row["name"].startswith("铭刻")
                  and not (class_name == "Hunter" and row.get("skill") == [])})
    flags = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        futures = [pool.submit(spell_flags, spell_id) for spell_id in ids]
        for future in concurrent.futures.as_completed(futures):
            spell_id, text = future.result()
            flags[str(spell_id)] = text
            if len(flags) % 50 == 0:
                print(f"已核对 {len(flags)}/{len(ids)} 个无学习来源法术", flush=True)
                (CACHE / "flags.json").write_text(json.dumps(flags, ensure_ascii=False, indent=2), encoding="utf-8")
    (CACHE / "flags.json").write_text(json.dumps(flags, ensure_ascii=False, indent=2), encoding="utf-8")
    generate(data, flags)
