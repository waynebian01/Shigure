local addon, ns = ...

local IsSpellKnown = C_SpellBook.IsSpellKnown
local IsSpellInSpellBook = C_SpellBook.IsSpellInSpellBook

local state = Fuyutsui.state
local EnumPowerType = Fuyutsui.EnumPowerType
local spellsList = Fuyutsui.spellsList

local drinkStatusTimer = nil

function Fuyutsui:GetCharacterInfo()
    self.db.char.level = UnitLevel("player")
    self.state.name = UnitName("player")
    self.state.GUID = UnitGUID("player")
    self.state.classColor = RAID_CLASS_COLORS[self.state.classFilename].colorStr
end

function Fuyutsui:GetCharacterSpecInfo()
    self.state.specIndex = C_SpecializationInfo.GetSpecialization()
    local specID, specName, _, _, role = C_SpecializationInfo.GetSpecializationInfo(self.state.specIndex)
    self.state.specID = specID
    self.state.specName = specName
    self.state.specRole = role
    self.state.specRange = self.rangeSpecID[specID]
    self.state.isDead = UnitIsDeadOrGhost("player")
    self.state.isChatOpen = false
    self.state.casting = false
    self.state.channeling = false
    self.state.mountCasting = false
    self:LoadPlayerBlocks(self.state.specIndex)
    self:UpdateSpellKnown()
    self:UpdatePlayerMounted()
    self:UpdatePlayerPet()
    self:UpdateGroup()
    -- 登录阶段其他插件可能稍后写入覆盖绑定；首次加载延后 5 秒，确保本插件最后绑定。
    C_Timer.After(5, function()
        self:LoadPlayerMacros()
    end)
    self:GetItemCount()
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
end

function Fuyutsui:UpdatePlayerSpecInfo()
    self:ClearAllTextures()
    self.state.specIndex = C_SpecializationInfo.GetSpecialization()
    local specID, specName, _, _, role = C_SpecializationInfo.GetSpecializationInfo(self.state.specIndex)
    self.state.specID = specID
    self.state.specName = specName
    self.state.specRole = role
    self.state.specRange = self.rangeSpecID[specID]
    self:LoadPlayerBlocks(self.state.specIndex)
    self:UpdateSpellKnown()
    self:UpdatePlayerBlocks()
    self:LoadPlayerMacros()
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
end

function Fuyutsui:UpdatePlayerValid()
    local valid = not state.isDead and not state.mounted and not state.isChatOpen and not state.drinkStatus and
        not state.mountCasting
    state.valid = valid and 1 / 255 or 0
    self:UpdateStateBlock("状态", "有效性")
end

function Fuyutsui:UpdatePlayerCombat()
    state.combat = UnitAffectingCombat("player")
end

function Fuyutsui:UpdatePlayerCombatTime()
    if state.combat then
        local combatTime = GetTime() - state.combatStartTime
        state.combatTime = math.min(1, combatTime / 255)
    else
        state.combatTime = 0
    end
    self:UpdateStateBlock("状态", "战斗时间")
end

function Fuyutsui:UpdatePlayerMoving(boolean)
    state.drinkStatus = false
    self:UpdatePlayerValid()
    state.moving = boolean and 1 / 255 or 0
    self:UpdateStateBlock("状态", "移动")
end

function Fuyutsui:UpdatePlayerCastBlocks()
    self:UpdateStateBlock("状态", "施法(正计时)")
    self:UpdateStateBlock("状态", "施法(倒计时)")
    self:UpdateStateBlock("状态", "引导")
    self:UpdateStateBlock("状态", "蓄力")
    self:UpdateStateBlock("状态", "蓄力层数")
end

function Fuyutsui:UpdatePlayerCastingInfo()
    self:UpdateStateBlock("状态", "施法(正计时)")
    self:UpdateStateBlock("状态", "施法(倒计时)")
end

function Fuyutsui:UpdatePlayerChannelingInfo()
    self:UpdateStateBlock("状态", "引导")
end

function Fuyutsui:UpdatePlayerEmpowerInfo()
    self:UpdateStateBlock("状态", "蓄力")
    self:UpdateStateBlock("状态", "蓄力层数")
end

function Fuyutsui:UpdatePlayerHealth()
    local healthPercent = UnitHealthPercent("player", false, self.curve100)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    state.healthPercent = b
    self:UpdateStateBlock("状态", "生命值")
end

function Fuyutsui:UpdatePlayerPower(powerType)
    local blocks = self.blocks
    if not blocks then return end
    local powerName = self.powerNameMap[powerType]
    local power = UnitPower("player", EnumPowerType[powerType])
    if not powerName then return end
    if issecretvalue(power) then
        if not self.powerCurves[powerType] then self:CreatePowerCurve(powerType) end
        local powerPercent = UnitPowerPercent("player", EnumPowerType[powerType], nil, self.powerCurves[powerType])
        ---@diagnostic disable-next-line: param-type-mismatch
        local _, _, b = powerPercent:GetRGB()
        state.power[powerType] = b
        self:UpdateBareStateBlock(powerName, { "能量", "状态" })
    else
        state.power[powerType] = power / 255
        self:UpdateBareStateBlock(powerName, { "能量", "状态" })
    end
end

function Fuyutsui:UpdateChargedComboPoints()
    local chargedPoints = GetUnitChargedPowerPoints("player")
    state.chargedComboPoints = (chargedPoints and #chargedPoints or 0) / 255
    self:UpdateStateBlock("能量", "增压层数")
end

function Fuyutsui:UpdatePlayerPowerType()
    state.power = {}
    for powerType in pairs(EnumPowerType) do
        self:CreatePowerCurve(powerType)
        self:UpdatePlayerPower(powerType)
    end
end

local empowerSpellId = {
    [355936] = true,  -- 梦境吐息
    [357208] = true,  -- 火焰吐息
    [382266] = true,  -- 火焰吐息
    [382411] = true,  -- 永恒之涌
    [396286] = true,  -- 地壳激变
    [1263824] = true, -- 吞噬
}
local assistantWasEmpower = false
local assistantSuppressUntil = 0

function Fuyutsui:UpdatePlayerAssistant()
    local spellId = C_AssistedCombat.GetNextCastSpell()
    local now = GetTime()

    -- 离开蓄力推荐后，强制显示 0 持续 0.5 秒
    if assistantSuppressUntil > 0 and now < assistantSuppressUntil then
        if empowerSpellId[spellId] then
            assistantSuppressUntil = 0
        else
            state.assistantSpell = 0
            self:UpdateStateBlock("状态", "一键辅助")
            return
        end
    else
        assistantSuppressUntil = 0
    end

    if empowerSpellId[spellId] then
        assistantWasEmpower = true
        local spellIndex = spellsList[spellId] and spellsList[spellId].index or 0
        state.assistantSpell = spellIndex / 255 or 0
        self:UpdateStateBlock("状态", "一键辅助")
        return
    end

    if assistantWasEmpower then
        assistantWasEmpower = false
        assistantSuppressUntil = now + 0.7
        state.assistantSpell = 0
        self:UpdateStateBlock("状态", "一键辅助")
        return
    end

    local spellIndex = spellsList[spellId] and spellsList[spellId].index or 0
    state.assistantSpell = spellIndex / 255 or 0
    self:UpdateStateBlock("状态", "一键辅助")
end

function Fuyutsui:UpdateGroupType()
    local index = 0
    if UnitInRaid("player") then
        index = UnitInRaid("player") or 0
    elseif UnitInParty("player") then
        index = 46
    end
    state.groupType = index / 255 or 0
    self:UpdateStateBlock("状态", "队伍类型")
end

function Fuyutsui:UpdateGroupCount()
    local count = GetNumGroupMembers()
    state.groupCount = count / 255 or 0
    self:UpdateStateBlock("状态", "队伍人数")
end

function Fuyutsui:UpdateEncounterID(encounterID, difficultyID)
    state.encounterID = encounterID
    local id = self.bossID and self.bossID[encounterID] or 0
    if id then
        state.bossID = id / 255 or 0
    else
        state.bossID = 0
    end
    self:UpdateStateBlock("状态", "首领战")
    state.difficultyID = difficultyID
    self:UpdateStateBlock("状态", "难度")
end

function Fuyutsui:UpdateHeroTalent()
    if self.heroTalents then
        C_Timer.After(1, function()
            self.state.heroTalent = 0
            for spellID, index in pairs(self.heroTalents) do
                if IsSpellKnown(spellID) or IsSpellInSpellBook(spellID) then
                    self.state.heroTalent = index
                    break
                end
            end
            self:UpdateStateBlock("状态", "英雄天赋")
        end)
    end
end

function Fuyutsui:UpdatePlayerBarInfo()
    local blocks = self.blocks
    if self.RefreshPlayerAuraContainers then
        self:RefreshPlayerAuraContainers()
    end
    if blocks and blocks.bars then
        for _, v in ipairs(blocks.bars) do
            self:CreateAutoLayoutBar(v.valueType, v.minValue, v.maxValue, v.spellId)
        end
    end
    if self.LayoutAuraApplicationBars then
        self:LayoutAuraApplicationBars()
    end
end

function Fuyutsui:UpdatePlayerPet()
    self:UpdateUnitType("pet")
    self:UpdateUnitHealthBlock("pet")
end

function Fuyutsui:UpdatePlayerMounted()
    state.mounted = IsMounted() or state.shapeshiftFormID == 27 or state.shapeshiftFormID == 3 or
        state.shapeshiftFormID == 29
    self:UpdatePlayerValid()
end

function Fuyutsui:UpdatePlayerCasting(spellId)
    local castingSpell = spellsList[spellId] and spellsList[spellId].index or 0
    state.castingSpell = castingSpell / 255 or 0
    self:UpdateStateBlock("状态", "施法目标")
    self:UpdateStateBlock("状态", "施法技能")
end

function Fuyutsui:UpdatePlayerConfig()
    if not (self.db and self.db.char) then return end
    local names = { "爆发开关", "AOE开关", "输出模式", "爆发药水开关" }
    for i = 1, #names do
        self:UpdateBareStateBlock(names[i], { "配置开关", "状态" })
    end
end

function Fuyutsui:UpdatePlayerStagger()
    local unit = "player"
    local damage = UnitStagger(unit)
    local maxHealth = UnitHealthMax(unit)
    if issecretvalue(damage) or issecretvalue(maxHealth) then
        state.staggerPercent = 0
        self:UpdateStateBlock("状态", "酒池")
        return
    end
    local staggerPercent = damage / maxHealth * 100
    state.staggerPercent = staggerPercent / 255 or 0
    self:UpdateStateBlock("状态", "酒池")
end

local holyArmaments = {
    [432459] = 1, -- 神圣壁垒
    [432472] = 2, -- 圣洁武器
}

function Fuyutsui:UpdateHolyArmaments(spellID) -- 神圣军备
    if not spellID or spellID ~= 375576 then return end
    for spellId, index in pairs(holyArmaments) do
        local overrideSpellID = C_Spell.GetOverrideSpell(375576)
        if not overrideSpellID then return end
        if overrideSpellID == spellId then
            state.holyArmaments = index / 255 or 0
            self:UpdateStateBlock("状态", "神圣军备")
        end
    end
end

function Fuyutsui:UpdateVampiricStrike(spellID) -- 吸血鬼打击
    if spellID == 206930 or spellID == 55090 then
        local overrideSpellID1 = C_Spell.GetOverrideSpell(206930)
        local overrideSpellID2 = C_Spell.GetOverrideSpell(55090)

        if overrideSpellID1 == 433895 or overrideSpellID2 == 433895 then
            state.VampiricStrike = 1 / 255
            self:UpdateStateBlock("状态", "吸血鬼打击")
        else
            state.VampiricStrike = 0
            self:UpdateStateBlock("状态", "吸血鬼打击")
        end
    end
end

function Fuyutsui:UpdateReaverGlaive(spellID) -- 收割者战刃
    if not spellID or spellID ~= 206930 then return end
    local overrideSpellID = C_Spell.GetOverrideSpell(204157)

    if overrideSpellID == 433895 then
        state.reaverGlaive = 1 / 255
        self:UpdateStateBlock("状态", "收割者战刃")
    else
        state.reaverGlaive = 0
        self:UpdateStateBlock("状态", "收割者战刃")
    end
end

function Fuyutsui:UpdateVampiricStrike(baseSpellID, overrideSpellID) -- 吸血鬼打击
    if not baseSpellID or baseSpellID ~= 55090 then return end
    if overrideSpellID == 433895 then
        state.vampiricStrike = 1 / 255
        self:UpdateStateBlock("状态", "吸血鬼打击")
    else
        state.vampiricStrike = 0
        self:UpdateStateBlock("状态", "吸血鬼打击")
    end
end

local heroicStrikeTimer = nil

function Fuyutsui:UpdateHeroicStrike(spellID) -- 英勇打击
    if not spellID or spellID ~= 1464 then return end

    if heroicStrikeTimer then
        heroicStrikeTimer:Cancel()
        heroicStrikeTimer = nil
    end

    local overrideSpellID = C_Spell.GetOverrideSpell(1464)
    if overrideSpellID == 1269383 then
        local remaining = 15
        state.heroicStrike = remaining / 255
        self:UpdateStateBlock("状态", "英勇打击")

        heroicStrikeTimer = C_Timer.NewTicker(1, function()
            remaining = remaining - 1
            state.heroicStrike = remaining > 0 and (remaining / 255) or 0
            self:UpdateStateBlock("状态", "英勇打击")
            if remaining <= 0 then
                heroicStrikeTimer = nil
            end
        end, 15)
    else
        state.heroicStrike = 0
        self:UpdateStateBlock("状态", "英勇打击")
    end
end

function Fuyutsui:UpdateRune()
    local total = 0
    for i = 1, 6 do
        local runeCount = GetRuneCount(i)
        if runeCount then
            total = total + runeCount
        end
    end
    state.runeCount = total / 255 or 0
    self:UpdateBareStateBlock("符文", { "能量", "状态" })
end

function Fuyutsui:UpdateShapeshiftForm()
    local shapeshiftFormID = GetShapeshiftFormID() or 0
    state.shapeshiftFormID = shapeshiftFormID / 255
    self:UpdateStateBlock("状态", "姿态")
end

function Fuyutsui:UpdateDrinkStatus(spellID)
    local name = C_Spell.GetSpellName(spellID)
    if name == "饮水" or name == "进食饮水" then
        state.drinkStatus = true
        self:UpdatePlayerValid()
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        drinkStatusTimer = C_Timer.NewTimer(20, function()
            state.drinkStatus = false
            self:UpdatePlayerValid()
            drinkStatusTimer = nil
        end)
    else
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        state.drinkStatus = false
        self:UpdatePlayerValid()
    end
end

-- 死亡骑士天启骑士检测
local ActiveKnightSpells = {
    [454393] = 1,
    [454389] = 2,
    [454392] = 3,
    [454390] = 4,
}
local InactiveKnightSpells = {
    [444248] = 1,
    [444251] = 2,
    [444252] = 3,
    [444254] = 4,
}
local ActiveKnights = { false, false, false, false }

function Fuyutsui:UpdateKnightStatus(spellID)
    if ActiveKnightSpells[spellID] then
        ActiveKnights[ActiveKnightSpells[spellID]] = true
    end
    if InactiveKnightSpells[spellID] then
        ActiveKnights[InactiveKnightSpells[spellID]] = false
    end
end

local function GetActiveKnightsCount()
    local count = 0
    for i = 1, 4 do
        if ActiveKnights[i] then
            count = count + 1
        end
    end
    return count
end

function Fuyutsui:UpdateKnightStatusCount()
    state.knightCount = GetActiveKnightsCount() / 255
    self:UpdateStateBlock("状态", "天启骑士数量")
end

function Fuyutsui:HookChatFrameEditBox()
    for i = 1, NUM_CHAT_WINDOWS do
        local editBox = _G["ChatFrame" .. i .. "EditBox"]
        if editBox then
            editBox:HookScript("OnEditFocusGained", function()
                state.isChatOpen = true
                self:UpdatePlayerValid()
            end)
            editBox:HookScript("OnEditFocusLost", function()
                state.isChatOpen = false
                self:UpdatePlayerValid()
            end)
        end
    end
end

local mounts = {}
local mountCastingTimer = nil

function Fuyutsui:GetMountsInfo()
    wipe(mounts)
    local mountIDs = C_MountJournal.GetMountIDs()
    for i = 1, #mountIDs do
        local _, spellID, _, _, _, _, _, _, _, _, isCollected = C_MountJournal.GetMountInfoByID(mountIDs[i])
        if isCollected and spellID then
            mounts[spellID] = true
        end
    end
end

function Fuyutsui:UpdateMountCasting(spellID, casting)
    if casting then
        if spellID and not issecretvalue(spellID) and mounts[spellID] then
            if mountCastingTimer then
                mountCastingTimer:Cancel()
                mountCastingTimer = nil
            end
            state.mountCasting = true
            self:UpdatePlayerValid()
        end
    elseif state.mountCasting then
        if mountCastingTimer then
            mountCastingTimer:Cancel()
        end
        mountCastingTimer = C_Timer.NewTimer(0.1, function()
            state.mountCasting = false
            self:UpdatePlayerValid()
            mountCastingTimer = nil
        end)
    end
end

local forbearanceTimer = nil

function Fuyutsui:UpdatePlayerForbearance() -- 25771 自律
    if forbearanceTimer then
        forbearanceTimer:Cancel()
        forbearanceTimer = nil
    end

    local remaining = 30
    state.forbearance = remaining / 255
    self:UpdateStateBlock("状态", "自律")

    forbearanceTimer = C_Timer.NewTicker(1, function()
        remaining = remaining - 1
        state.forbearance = remaining > 0 and (remaining / 255) or 0
        self:UpdateStateBlock("状态", "自律")
        if remaining <= 0 then
            forbearanceTimer = nil
        end
    end, 30)
end
