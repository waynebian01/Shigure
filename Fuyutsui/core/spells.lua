local addon, ns = ...

local GetSpellName = C_Spell.GetSpellName
local GetSpellCooldown = C_Spell.GetSpellCooldown
local GetSpellChargeDuration = C_Spell.GetSpellChargeDuration
local GetSpellCooldownDuration = C_Spell.GetSpellCooldownDuration
local EvaluateColorFromBoolean = C_CurveUtil.EvaluateColorFromBoolean

local IsSpellKnown = C_SpellBook.IsSpellKnown
local IsSpellInSpellBook = C_SpellBook.IsSpellInSpellBook

local target = Fuyutsui.target
local state = Fuyutsui.state

local spells = {}
local insertSpellTimer, insertSpellIndex = nil, nil

local ColorValue255 = CreateColor(0, 0, 1, 1)

local dispelCurve = C_CurveUtil.CreateColorCurve()
target.enemyCurve = C_CurveUtil.CreateColorCurve()
target.friendCurve = C_CurveUtil.CreateColorCurve()
dispelCurve:SetType(Enum.LuaCurveType.Step)
target.enemyCurve:SetType(Enum.LuaCurveType.Step)
target.friendCurve:SetType(Enum.LuaCurveType.Step)

local succSpells = {}
local succIndex = 1

local function DebugPrintNewSpellEntry(spellID)
    if succSpells[spellID] or Fuyutsui.spellsList[spellID] then return end
    succSpells[spellID] = true
    print("[" .. spellID .. "]" .. " = { index = " .. succIndex .. ", }, -- " .. GetSpellName(spellID))
    succIndex = succIndex + 1
end

local function DebugPrintSpellBlockLine(spellID)
    local spellName = C_Spell.GetSpellName(spellID)
    print("[] = { type = \"spell\", spellId = " .. spellID .. ", name = \"" .. spellName .. "\" },")
end

Fuyutsui.DebugPrintNewSpellEntry = DebugPrintNewSpellEntry
Fuyutsui.DebugPrintSpellBlockLine = DebugPrintSpellBlockLine

local overrideSpells = {
    [432459] = 1289728, -- 神圣壁垒
    [432472] = 1289728, -- 圣洁武器
    [444995] = 455630,  -- 涌动图腾
    [1242173] = 228260, -- 虚空齐射
    [1241314] = 31884,  -- 愤怒之锤
    [24275] = 31884,    -- 愤怒之锤
}

function Fuyutsui:IsSpellKnown(spellID)
    local isKnown = IsSpellKnown(spellID)
    if isKnown then
        return isKnown
    end
    local overrideSpellID = overrideSpells[spellID]
    if overrideSpellID then
        isKnown = IsSpellKnown(overrideSpellID)
    end
    return isKnown
end

function Fuyutsui:ClearInsertSpell()
    if insertSpellTimer then
        insertSpellTimer:Cancel()
        insertSpellTimer = nil
    end
    insertSpellIndex = nil
    state.insertSpell = 0
    self:UpdateStateBlock("状态", "插入法术")
end

--- index: spellsList 中的宏序号；spellName/unit 仅用于提示
function Fuyutsui:SetInsertSpell(index, spellName, unit)
    if insertSpellTimer then
        insertSpellTimer:Cancel()
        insertSpellTimer = nil
    end
    insertSpellIndex = index
    state.insertSpell = index / 255
    self:UpdateStateBlock("状态", "插入法术")
    local msg = "|cff00ff00[Fuyutsui]|r 插入法术: |cff00ff00" .. (spellName or "?") .. "|r"
    if unit and unit ~= "" then
        msg = msg .. " @" .. unit
    end
    print(msg)
    insertSpellTimer = C_Timer.NewTimer(1.5, function()
        insertSpellTimer = nil
        insertSpellIndex = nil
        state.insertSpell = 0
        Fuyutsui:UpdateStateBlock("状态", "插入法术")
    end)
end

function Fuyutsui:UpdateInsertSpellBySuccess(spellID)
    if not insertSpellIndex then return end
    local info = self.spellsList and self.spellsList[spellID]
    if not info or info.index ~= insertSpellIndex then return end
    self:ClearInsertSpell()
end

local dispelAbilities = {
    [1] = { 527, 360823, 4987, 115450, 88423, 77130 },
    [2] = { 383016, 51886, 392378, 2782, 475 },
    [3] = { 390632, 213634, 393024, 213644, 388874, 218164 },
    [4] = { 392378, 2782, 393024, 213644, 388874, 218164, 365585 },
    [11] = {},
}

local offensiveDispelAbilities = {
    [1] = { 528 },
    [9] = { 2908 },
}

local function HasLearnedAnySpell(spellIDs)
    for _, spellID in ipairs(spellIDs) do
        if IsSpellKnown(spellID) then
            return true
        end
    end
    return false
end



local function UpdateCooldownSpellKnown()
    spells = {}
    if not Fuyutsui.blocks or not Fuyutsui.blocks.spells then return end
    C_Timer.After(1, function()
        local blocks = Fuyutsui.blocks
        if not blocks or not blocks.spells then return end
        for spellID, info in pairs(blocks.spells) do
            local isKnown = Fuyutsui:IsSpellKnown(spellID)
            if info.inSpellBook then
                isKnown = IsSpellInSpellBook(spellID)
            end
            if isKnown or info.forcedKnown then
                spells[spellID] = info
            else
                if info.index then
                    Fuyutsui:CreateTexture(info.index, 1)
                end
                if info.charge then
                    Fuyutsui:CreateTexture(info.charge, 1)
                end
            end
        end
    end)
end

local DEFENSIVE_DISPEL_TYPE_NAMES = {
    [1] = "Magic",
    [2] = "Curse",
    [3] = "Disease",
    [4] = "Poison",
    [11] = "Bleed",
}

local OFFENSIVE_DISPEL_TYPE_NAMES = {
    [1] = "Magic",
    [9] = "Enrage",
}

function Fuyutsui:UpdateSpellKnown()
    UpdateCooldownSpellKnown()

    local dispelCapabilities = {
        [1] = false,
        [2] = false,
        [3] = false,
        [4] = false,
        [11] = false,
    }
    local offensiveDispelCapabilities = {
        [1] = false,
        [9] = false,
    }

    for debuffType, spellIDs in pairs(dispelAbilities) do
        dispelCapabilities[debuffType] = HasLearnedAnySpell(spellIDs)
    end

    for debuffType, spellIDs in pairs(offensiveDispelAbilities) do
        offensiveDispelCapabilities[debuffType] = HasLearnedAnySpell(spellIDs)
    end

    self.dispelCapabilities = dispelCapabilities
    self.offensiveDispelCapabilities = offensiveDispelCapabilities

    local includeDispelTypes = {}
    for id, can in pairs(dispelCapabilities) do
        local name = DEFENSIVE_DISPEL_TYPE_NAMES[id]
        if can and name then
            includeDispelTypes[name] = true
        end
    end
    self.includeDispelTypes = includeDispelTypes

    local includeOffensiveDispelTypes = {}
    for id, can in pairs(offensiveDispelCapabilities) do
        local name = OFFENSIVE_DISPEL_TYPE_NAMES[id]
        if can and name then
            includeOffensiveDispelTypes[name] = true
        end
    end
    self.includeOffensiveDispelTypes = includeOffensiveDispelTypes

    dispelCurve:ClearPoints()
    target.enemyCurve:ClearPoints()
    target.friendCurve:ClearPoints()

    for i, v in pairs(dispelCapabilities) do
        if v then
            dispelCurve:AddPoint(i, CreateColor(0, 1, i / 255, 1))
            target.friendCurve:AddPoint(i, CreateColor(0, 1, (i + 11) / 255, 1))
        else
            dispelCurve:AddPoint(i, CreateColor(0, 0, 0, 1))
            target.friendCurve:AddPoint(i, CreateColor(0, 0, 11 / 255, 1))
        end
    end

    for i, v in pairs(offensiveDispelCapabilities) do
        if v then
            if i == 9 then
                target.enemyCurve:AddPoint(9, CreateColor(0, 1, 3 / 255, 1))
            else
                target.enemyCurve:AddPoint(i, CreateColor(0, 1, (i + 1) / 255, 1))
            end
        else
            target.enemyCurve:AddPoint(i, CreateColor(0, 0, 1 / 255, 1))
        end
    end
end

function Fuyutsui:UpdateSpellCooldown()
    if not spells then return end
    local curve255 = self.curve255
    for spellID, info in pairs(spells) do
        local index = info.index
        -- charge-only 条目只有 .charge，没有冷却像素索引
        if index then
            local cdDurationObj = GetSpellCooldownDuration(spellID)
            local cdInfo = GetSpellCooldown(spellID)
            if cdDurationObj and cdInfo then
                local result = cdDurationObj:EvaluateRemainingDuration(curve255, 1)
                ColorValue255:SetRGBA(0, index / 255, 254 / 255)
                ---@diagnostic disable-next-line: param-type-mismatch
                local value = EvaluateColorFromBoolean(cdInfo.isEnabled, result, ColorValue255)
                local _, _, b = value:GetRGB()
                ---@diagnostic disable-next-line: undefined-field
                if cdInfo.isOnGCD then b = 0 end
                self:CreateTexture(index, b)
            else
                self:CreateTexture(index, 1)
            end
        end
        local chargeIndex = info.charge
        if chargeIndex then
            local chDurationObj = GetSpellChargeDuration(spellID)
            if chDurationObj then
                local result = chDurationObj:EvaluateRemainingDuration(curve255)
                ---@diagnostic disable-next-line: param-type-mismatch
                local _, _, b = result:GetRGB()
                self:CreateTexture(chargeIndex, b)
            else
                self:CreateTexture(chargeIndex, 1)
            end
        end
    end
end

function Fuyutsui:GetItemCount()
    self.state.HealthPotionCount = C_Item.GetItemCount(241304) + C_Item.GetItemCount(241305) +
        C_Item.GetItemCount(271884) + C_Item.GetItemCount(271885)
    self.state.ManaPotionCount = C_Item.GetItemCount(241301) + C_Item.GetItemCount(241300)
    self.state.HealthstoneCount = C_Item.GetItemCount(5512) + C_Item.GetItemCount(224464)
    self.state.RecklessnessCount = C_Item.GetItemCount(241288) + C_Item.GetItemCount(241289)
    self.state.LightsPotentialCount = C_Item.GetItemCount(241308) + C_Item.GetItemCount(241309)
    self.state.DraughtOfRampantAbandonCount = C_Item.GetItemCount(241292) + C_Item.GetItemCount(241293)
end

function Fuyutsui:GetItemRemainingTime(itemID)
    local startTimeSeconds, durationSeconds, enableCooldownTimer = C_Item.GetItemCooldown(itemID)
    if not enableCooldownTimer then return 255 end
    if startTimeSeconds > 0 then
        return durationSeconds - (GetTime() - startTimeSeconds)
    else
        return 0
    end
end

function Fuyutsui:UpdateItemCooldown()
    local itemNames = { "治疗药水", "魔法药水", "治疗石", "鲁莽药水", "圣光潜力" }
    for _, name in ipairs(itemNames) do
        self:UpdateBareStateBlock(name, { "物品", "状态" })
    end
end
