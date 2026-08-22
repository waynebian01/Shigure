local addon, ns = ...

local fuDelayEndTimer = nil

function Fuyutsui:GetCharConfig()
    return self.db and self.db.char
end

function Fuyutsui:NormalizeCharConfig()
    local c = self:GetCharConfig()
    if not c then return end
    c.aoeMode = c.aoeMode or 0
    c.cooldowns = c.cooldowns or 0
    c.dpsMode = c.dpsMode or 0
    c.delay = c.delay or 0
    c.potion = c.potion or 0
end

--- 通用角色开关：规范化、同步像素、刷新快捷按钮
function Fuyutsui:SwitchCharFlag(key, offMsg, onMsg, blockName)
    local c = self:GetCharConfig()
    if not c then return end
    if c[key] == 0 then
        print(offMsg)
    else
        print(onMsg)
    end
    if blockName and self.UpdateBareStateBlock then
        self:UpdateBareStateBlock(blockName, { "配置开关", "状态" })
    end
    self:NormalizeCharConfig()
    if self.RefreshQuickToggleAppearance then
        self:RefreshQuickToggleAppearance()
    end
end

function Fuyutsui:SwitchCooldown()
    self:SwitchCharFlag(
        "cooldowns",
        "|cff00ff00[Fuyutsui]|r 爆发已|cffff0000关闭|r",
        "|cff00ff00[Fuyutsui]|r 爆发已|cff00ff00开启|r",
        "爆发开关"
    )
end

function Fuyutsui:SwitchAoeMode()
    self:SwitchCharFlag(
        "aoeMode",
        "|cff00ff00[Fuyutsui]|r 已切换|cff00ff00自动|r模式！",
        "|cff00ff00[Fuyutsui]|r 已切换|cff00ff00单体|r模式！",
        "AOE开关"
    )
end

function Fuyutsui:SwitchPotion()
    self:SwitchCharFlag(
        "potion",
        "|cff00ff00[Fuyutsui]|r 药水已|cffff0000关闭|r",
        "|cff00ff00[Fuyutsui]|r 药水已|cff00ff00开启|r",
        "爆发药水开关"
    )
end

function Fuyutsui:SwitchDelay()
    local c = self:GetCharConfig()
    if not c then return end
    if self.UpdateBareStateBlock then
        self:UpdateBareStateBlock("延迟", { "配置开关", "状态" })
    end
    self:NormalizeCharConfig()
end

local function IsUnitToken(token)
    if not token or token == "" then return false end
    local t = string.lower(token)
    if t == "player" or t == "target" or t == "focus"
        or t == "mouseover" or t == "cursor" or t == "pet" then
        return true
    end
    return t:match("^party%d+$") or t:match("^raid%d+$") or t:match("^boss%d+$")
end

--- 从宏条目提取核心技能名：去掉 [条件] 与 ; 分支
local function ExtractMacroSpellName(entry)
    local s = entry:match("([^;]+)$") or entry
    s = s:gsub("^%s*%[.-%]%s*", "")
    return strtrim(s)
end

local function MacroEntryHasSpell(entry, spellName)
    if type(entry) ~= "string" or spellName == "" then return false end
    if ExtractMacroSpellName(entry) == spellName then return true end
    return entry:find(spellName, 1, true) ~= nil
end

local function MacroEntryMatchesUnit(entry, unit)
    if not unit or unit == "" then return true end
    if not entry:find("%[", 1, true) then return true end
    local e = string.lower(entry)
    local u = string.lower(unit)
    return e:find("@" .. u, 1, true) ~= nil or e:find("target=" .. u, 1, true) ~= nil
end

local function FindSpellListByName(spellName)
    local list = Fuyutsui.spellsList
    if not list then return nil end
    for spellId, info in pairs(list) do
        if type(info) == "table" and info.name == spellName and info.index then
            return info.index, spellId
        end
    end
    return nil
end

local function IsSpellInClassMacros(spellName, unit)
    local m = Fuyutsui.MacrosList
    if not m then
        local classFile = UnitClassBase("player")
        m = Fuyutsui.ClassMacros and Fuyutsui.ClassMacros[classFile]
    end
    if not m then return false end

    local function checkList(list)
        if type(list) ~= "table" then return false end
        for _, entry in ipairs(list) do
            if MacroEntryHasSpell(entry, spellName) and MacroEntryMatchesUnit(entry, unit) then
                return true
            end
        end
        return false
    end

    return checkList(m.dynamicSpells) or checkList(m.staticSpells) or checkList(m.specialSpells)
end

function Fuyutsui:InsertSpellCommand(rest)
    rest = strtrim(rest or "")
    if rest == "" then
        print("|cff00ff00[Fuyutsui]|r 用法: /fu i 技能名称 [单位]")
        return
    end

    local spellName, unit
    local last = rest:match("(%S+)$")
    if last and IsUnitToken(last) and rest:find("%s", 1, true) then
        unit = string.lower(last)
        spellName = strtrim(rest:sub(1, #rest - #last))
    else
        spellName = rest
    end

    if spellName == "" then
        print("|cff00ff00[Fuyutsui]|r 用法: /fu i 技能名称 [单位]")
        return
    end

    local index = FindSpellListByName(spellName)
    if not index then
        print("|cff00ff00[Fuyutsui]|r 未在 spellsList 中找到技能: " .. spellName)
        return
    end

    if not IsSpellInClassMacros(spellName, unit) then
        if unit then
            print("|cff00ff00[Fuyutsui]|r 未在 ClassMacros 中找到技能（或单位不匹配）: "
                .. spellName .. " @" .. unit)
        else
            print("|cff00ff00[Fuyutsui]|r 未在 ClassMacros 中找到技能: " .. spellName)
        end
        return
    end

    self:SetInsertSpell(index, spellName, unit)
end

--- /fu cd 系列命令统一动作：写 Fuyutsui.BurstTime 唯一真相 + 镜像 c.cooldowns + 打印
--- 含时长文案 + 刷新 stateblock「爆发开关」像素 + 刷新快捷按钮显示态（不调用 SwitchCooldown）
local function SetBurstTime(c, seconds, cooldown, text)
    Fuyutsui.BurstTime = GetTime() + seconds
    c.cooldowns = cooldown
    print(text)
    if Fuyutsui.UpdateBareStateBlock then
        Fuyutsui:UpdateBareStateBlock("爆发开关", { "配置开关", "状态" })
    end
    if Fuyutsui.RefreshQuickToggleAppearance then
        Fuyutsui:RefreshQuickToggleAppearance()
    end
end

function Fuyutsui:SlashCommand(input, editbox)
    input = strtrim(input or "")
    local command = string.lower(input)

    local c = self:GetCharConfig()
    if command == "cd" then
        if not c then return end
        SetBurstTime(c, 15, 1, "|cff00ff00[Fuyutsui]|r 爆发已开启（15 秒）")
    elseif command == "cd on" then
        if not c then return end
        SetBurstTime(c, 3600, 1, "|cff00ff00[Fuyutsui]|r 爆发已开启（3600 秒）")
    elseif command == "cd off" then
        if not c then return end
        SetBurstTime(c, -1, 0, "|cff00ff00[Fuyutsui]|r 爆发已关闭")
    elseif command:match("^cd%s+") then
        if not c then return end
        local secStr = command:match("^cd%s+(.+)$")
        local sec = tonumber(strtrim(secStr or ""))
        if not sec then
            print("|cff00ff00[Fuyutsui]|r 无效秒数；请输入数字（例如 /fu cd 30），或使用 /fu cd on / /fu cd off。")
        elseif sec > 0 then
            SetBurstTime(c, sec, 1, "|cff00ff00[Fuyutsui]|r 爆发已开启（" .. sec .. " 秒）")
        else
            SetBurstTime(c, -1, 0, "|cff00ff00[Fuyutsui]|r 爆发已关闭")
        end
    elseif command == "aoemode" then
        if not c then return end
        c.aoeMode = (c.aoeMode == 0) and 1 or 0
        self:SwitchAoeMode()
    elseif command == "aoemode auto" then
        if not c then return end
        c.aoeMode = 0
        self:SwitchAoeMode()
    elseif command == "aoemode aoe" then
        if not c then return end
        c.aoeMode = 1
        self:SwitchAoeMode()
    elseif command == "potion" then
        if not c then return end
        c.potion = (c.potion == 0) and 1 or 0
        self:SwitchPotion()
    elseif command == "potion on" then
        if not c then return end
        c.potion = 1
        self:SwitchPotion()
    elseif command == "potion off" then
        if not c then return end
        c.potion = 0
        self:SwitchPotion()
    elseif command == "hide" then
        self:HideQuickToggleButton()
    elseif command == "show" then
        self:ShowQuickToggleButton()
    elseif command:match("^delay") then
        if not c then return end
        local secStr = command:match("^delay%s+(.+)$")
        local sec = 1
        if secStr then
            local trimmed = strtrim(secStr)
            if trimmed ~= "" then
                local parsed = tonumber(trimmed)
                if parsed and parsed > 0 then
                    sec = parsed
                else
                    print("|cff00ff00[Fuyutsui]|r 无效秒数；请输入正数（例如 /fu delay 5），或不写秒数使用默认 1 秒。")
                    return
                end
            end
        end
        local delayAlreadyActive = fuDelayEndTimer ~= nil
        if fuDelayEndTimer then
            fuDelayEndTimer:Cancel()
            fuDelayEndTimer = nil
        end
        c.delay = 1
        self:SwitchDelay()
        fuDelayEndTimer = C_Timer.NewTimer(sec, function()
            fuDelayEndTimer = nil
            local cc = Fuyutsui:GetCharConfig()
            if cc then
                cc.delay = 0
                print("延迟已恢复。")
                Fuyutsui:SwitchDelay()
            end
        end)
        if not delayAlreadyActive then
            print("延迟已生效，" .. sec .. " 秒后恢复。")
        end
    elseif command == "i" or command:match("^i%s+") then
        -- 从原始 input 解析技能名（保留中文与大小写单位）
        local rest = input:match("^[iI]%s+(.*)$") or ""
        self:InsertSpellCommand(rest)
    elseif command == "help" or command == "" then
        print("|cff00ff00Fuyutsui|r 命令列表:")
        print("爆发开关（开启 15 秒）: /fu cd")
        print("|cff00ff00开启|r爆发（长计时 3600 秒）: /fu cd on")
        print("|cffff0000关闭|r爆发: /fu cd off")
        print("按秒开启爆发: /fu cd xx（xx 为秒数，<=0 等同关闭）")
        print("切换AOE模式: /fu aoemode")    
        print("切换AOE为|cff00ff00自动|r: /fu aoemode auto")
        print("切换AOE为|cff00ff00单体|r: /fu aoemode aoe")
        print("爆发药水开关: /fu potion")
        print("|cff00ff00开启|r药水: /fu potion on")
        print("|cffff0000关闭|r药水: /fu potion off")
        print("隐藏快捷控件: /fu hide")
        print("显示快捷控件: /fu show")
        print("临时 delay 标志（db.char.delay 置 1 持续 x 秒后归零）: /fu delay [秒]，省略秒数则为 1 秒")
        print("插入法术: /fu i 技能名称 [单位]")
        print("帮助: /fu help")
    else
        print("输入 /fu help 查看命令。")
    end
end
