"""校验九职业技能等级、技能组索引和生成配置；不修改文件。"""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
report = json.loads((ROOT / "Forever/reference/职业技能等级来源.json").read_text(encoding="utf-8"))
total = 0
for cls in report["classes"]:
    path = ROOT / "Forever/Shingen/class" / (cls["class"] + ".lua")
    source = path.read_text(encoding="utf-8")
    table = re.search(r"Shingen\.spellsList\s*=\s*\{(.*?)\n\}", source, re.S)[1]
    rows = re.findall(r'\[(\d+)\]\s*=\s*\{\s*index\s*=\s*(\d+),\s*name\s*=\s*("(?:\\.|[^"\\])*"),\s*Rank\s*=\s*(\d+)\s*\}', table)
    parsed = {int(spell_id): {"index": int(index), "name": json.loads(name), "Rank": int(rank)} for spell_id, index, name, rank in rows}
    order = [(json.loads(name), int(rank), int(spell_id)) for spell_id, _, name, rank in rows]
    assert order == sorted(order), f"{path}: 未按 name、Rank、spellId 排序"
    assert len(parsed) == len(rows) == len(cls["spells"]), f"{path}: 重复或未识别条目"
    groups = {}
    indices = {}
    for spell in cls["spells"]:
        value = parsed[spell["spellId"]]
        assert value == {key: spell[key] for key in ["index", "name", "Rank"]}, f"{path}: 与来源记录不同"
        assert 1 <= value["index"] <= 255 and 1 <= value["Rank"] <= 255
        groups.setdefault(spell["family"], set()).add(value["index"])
        indices.setdefault(value["index"], set()).add(spell["family"])
    assert all(len(group) == 1 for group in groups.values()), f"{path}: 技能组索引不一致"
    assert all(len(group) == 1 for group in indices.values()), f"{path}: 不同技能共用索引"
    state_fields = re.search(r'\["状态"\]\s*=\s*\{([^}]*)\}', source)[1]
    assert '"施法技能"' in state_fields and '"技能等级"' in state_fields
    config = json.loads((ROOT / "Forever/config" / (cls["class"] + ".json")).read_text(encoding="utf-8-sig"))
    # 一个职业一个 JSON 文件；永久服内部槽位为 1。
    class_config = config
    assert class_config["1"]["技能等级"]["type"] == "int"
    for index in indices:
        spells = [(spell_id, entry) for spell_id, entry in parsed.items() if entry["index"] == index]
        representative = min(spells, key=lambda item: (item[1]["Rank"], item[0]))[0]
        assert class_config["一键法术"][str(index)] == representative
    if cls["class"] == "Warlock":
        assert parsed[686] == {"index": 3, "name": "暗影箭", "Rank": 1}
        assert parsed[695] == {"index": 3, "name": "暗影箭", "Rank": 2}
    total += len(parsed)
    print(f"PASS {cls['class']}: {len(parsed)} 条技能、{len(groups)} 组、最大索引 {max(indices)}")
print(f"PASS 九职业共 {total} 条，技能等级、共享索引、状态字段及配置映射全部一致")
