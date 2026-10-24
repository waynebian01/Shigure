# Forever 职业数据

`Shingen/class/` 只保留九个经典职业。每个文件的 `ClassBlocks[1]` 是像素协议使用的固定槽位，代表整个职业，不代表专精；运行时统一写入值 `1`。职业技能、冷却与宏均按职业维护。

技能表使用 1–60 级经典主动技能，并补充已核对法术 ID 的经典天赋主动技能。基础法术 ID 对照 [ForeverDB 职业数据](https://foreverdb.net/class/Druid) 中的九个职业页面，天赋法术 ID 逐条对照其法术页面；简体中文名称参考 [LibBabble-Spell zhCN](https://github.com/Plan414/World-of-Warcraft-3.3.5-Addons/blob/main/ZHunterMod/Libs/LibBabble-Spell-3.0/zhCN.lua)。被动效果、宠物专属技能、种族技能与新版技能不放入职业宏表。

`core/classmacros.lua` 使用 `"#法术ID"` 保存技能，并在同行注释中保留中文名称，供 keymap 转换器显示。插件运行时调用 `C_Spell.GetSpellName` 获取当前客户端语言的技能名。修改职业 Lua 后，用 `dotnet run --project .\Shigure.csproj -- --update-config` 与 `--update-keymap` 重新生成 `Forever/config`、`Forever/keymap`。


副本编号见[副本列表](副本列表.md)，按推荐等级由低到高排列；`config/dungeons.json` / `config/raids.json` 分别保存列表与来源，`core/config.lua` 的 `dungeonIndex` / `raidIndex` 保存已核实的游戏副本 ID 到编号的映射。
