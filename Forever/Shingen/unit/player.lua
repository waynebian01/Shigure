local addon, ns = ...

local IsSpellKnown = C_SpellBook.IsSpellKnown
local IsSpellInSpellBook = C_SpellBook.IsSpellInSpellBook

local state = Shingen.state
local EnumPowerType = Shingen.EnumPowerType
local spellsList = Shingen.spellsList

local drinkStatusTimer = nil
local classRange = {
    WARRIOR = 5, PALADIN = 5, ROGUE = 5, HUNTER = 35,
    PRIEST = 40, SHAMAN = 30, MAGE = 40, WARLOCK = 40, DRUID = 30,
}

function Shingen:InitializeCharacterState()
    self.db.char.level = UnitLevel("player")
    self.state.name = UnitName("player")
    self.state.GUID = UnitGUID("player")
    self.state.classColor = RAID_CLASS_COLORS[self.state.classFilename].colorStr
end

function Shingen:InitializeSpecializationState()
    -- Forever 只有职业。[1] 是现有像素协议/配置解析的固定内部槽位。
    self.state.specIndex = 1
    self.state.specID = 1
    self.state.specName = nil
    self.state.specRole = UnitGroupRolesAssigned("player") or "NONE"
    self.state.specRange = classRange[self.state.classFilename] or 30
    self.state.isDead = UnitIsDeadOrGhost("player")
    self.state.isChatOpen = false
    self.state.casting = false
    self.state.channeling = false
    self.state.empowering = false
    self.state.mountCasting = false
    self:LoadPlayerBlocks(self.state.specIndex)
    self:SetPlayerCastingSpell(0)
    self:UpdateSpellKnown()
    self:RefreshPlayerMountedState()
    self:RefreshPlayerPetState()
    self:RebuildGroupRoster()
    -- 登录阶段其他插件可能稍后写入覆盖绑定；首次加载延后 5 秒，确保本插件最后绑定。
    C_Timer.After(5, function()
        self:LoadPlayerMacros()
    end)
    self:UpdateItemCooldown()
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
    self:UpdateStateBlock("特殊", "倒数")
end

function Shingen:RebuildSpecializationState()
    self:ClearAllShingenBars()
    self:ClearAllTextures()
    self.state.specIndex = 1
    self.state.specID = 1
    self.state.specName = nil
    self.state.specRole = UnitGroupRolesAssigned("player") or "NONE"
    self.state.specRange = classRange[self.state.classFilename] or 30
    self:LoadPlayerBlocks(self.state.specIndex)
    self:UpdateSpellKnown()
    self:RefreshPlayerState()
    self:LoadPlayerMacros()
    self:UpdateStateBlock("状态", "职业")
    self:UpdateStateBlock("状态", "专精")
    self:UpdateStateBlock("特殊", "计时器")
    self:UpdateStateBlock("特殊", "循环计时器")
    self:UpdateStateBlock("特殊", "倒数")
    self:UpdateStateBlock("特殊", "战斗计时(秒)")
    self:UpdateStateBlock("特殊", "战斗计时(分)")
end

function Shingen:RefreshPlayerValidity()
    local valid = not state.isDead and not state.mounted and not state.isChatOpen and not state.drinkStatus and
        not state.mountCasting
    state.valid = valid and 1 / 255 or 0
    self:UpdateStateBlock("状态", "有效性")
end

function Shingen:RefreshPlayerCombatState()
    state.combat = UnitAffectingCombat("player")
end

function Shingen:RefreshPlayerCombatDuration()
    if state.combat then
        if not state.combatStartTime then
            state.combatStartTime = GetTime()
        end
        local combatTime = GetTime() - state.combatStartTime
        if combatTime < 0 then
            combatTime = 0
        end
        state.combatTime = math.min(255, math.ceil(combatTime)) / 255
        local elapsedSec = math.floor(combatTime)
        state.combatTimerSec = ((elapsedSec % 60) + 1) / 255
        state.combatTimerMin = math.min(255, math.floor(elapsedSec / 60)) / 255
    else
        state.combatTime = 0
        state.combatTimerSec = 0
        state.combatTimerMin = 0
    end
    self:UpdateStateBlock("状态", "战斗时间")
    self:UpdateStateBlock("特殊", "战斗计时(秒)")
    self:UpdateStateBlock("特殊", "战斗计时(分)")
end

function Shingen:SetPlayerMoving(isMoving)
    state.drinkStatus = false
    self:RefreshPlayerValidity()
    state.moving = isMoving and 1 / 255 or 0
    self:UpdateStateBlock("状态", "移动")
end

function Shingen:RefreshPlayerCastStateBlocks()
    if state.casting then
        self:RefreshPlayerCastingStateBlocks()
    end
    if state.channeling then
        self:RefreshPlayerChannelStateBlock()
    end
    if state.empowering then
        self:RefreshPlayerEmpowerStateBlocks()
    end
end

function Shingen:RefreshPlayerCastingStateBlocks()
    self:UpdateStateBlock("状态", "施法(正计时)")
    self:UpdateStateBlock("状态", "施法(倒计时)")
end

function Shingen:RefreshPlayerChannelStateBlock()
    self:UpdateStateBlock("状态", "引导")
end

function Shingen:RefreshPlayerEmpowerStateBlocks()
    self:UpdateStateBlock("状态", "蓄力")
    self:UpdateStateBlock("状态", "蓄力层数")
end

function Shingen:UpdatePlayerHealth()
    local healthPercent = UnitHealthPercent("player", true, self.curve100)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    state.healthPercent = b
    self:UpdateStateBlock("状态", "生命值")
end

function Shingen:UpdatePlayerPower(powerType)
    local blocks = self.blocks
    if not blocks then return end
    local powerName = self.powerNameMap[powerType]
    if not powerName then return end
    -- 法力始终通过百分比曲线编码，避免普通数值分支输出绝对法力并溢出蓝色通道。
    if powerType == "MANA" then
        if not self.powerCurves[powerType] then self:CreatePowerCurve(powerType) end
        local powerPercent = UnitPowerPercent("player", EnumPowerType[powerType], nil, self.powerCurves[powerType])
        ---@diagnostic disable-next-line: param-type-mismatch
        local _, _, b = powerPercent:GetRGB()
        state.power[powerType] = b
    else
        local power = UnitPower("player", EnumPowerType[powerType])
        if issecretvalue(power) then
            if not self.powerCurves[powerType] then self:CreatePowerCurve(powerType) end
            local powerPercent = UnitPowerPercent("player", EnumPowerType[powerType], nil, self.powerCurves[powerType])
            ---@diagnostic disable-next-line: param-type-mismatch
            local _, _, b = powerPercent:GetRGB()
            state.power[powerType] = b
        else
            state.power[powerType] = power / 255
        end
    end
    self:UpdateBareStateBlock(powerName, { "能量", "状态" })
end

function Shingen:RefreshChargedComboPoints()
    local chargedPoints = GetUnitChargedPowerPoints("player")
    state.chargedComboPoints = (chargedPoints and #chargedPoints or 0) / 255
    self:UpdateStateBlock("能量", "增压层数")
end

function Shingen:RefreshAllPlayerPowers()
    state.power = {}
    self.powerCurves = {}
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

function Shingen:RefreshAssistedCombatSuggestion()
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

function Shingen:RefreshGroupTypeState()
    local index = 0
    if UnitInRaid("player") then
        index = UnitInRaid("player") or 0
    elseif UnitInParty("player") then
        index = 46
    end
    state.groupType = index / 255 or 0
    self:UpdateStateBlock("状态", "队伍类型")
end

function Shingen:RefreshGroupCountState()
    local count = GetNumGroupMembers()
    state.groupCount = count / 255 or 0
    self:UpdateStateBlock("状态", "队伍人数")
end

function Shingen:RefreshPlayerBars()
    local blocks = self.blocks
    if self.RefreshPlayerAuraContainers then
        self:RefreshPlayerAuraContainers()
    end
    if blocks and blocks.counts then
        for _, v in ipairs(blocks.counts) do
            self:CreateCountPixel(v.valueType, v.spellId, v.index)
        end
    end
end

function Shingen:RefreshPlayerPetState()
    self:RefreshUnitTypeState("pet")
    self:RefreshUnitHealthState("pet")
    self:RefreshUnitPowerState("pet")
end

function Shingen:RefreshPlayerMountedState()
    state.mounted = IsMounted() or state.shapeshiftFormID == 27 or state.shapeshiftFormID == 3 or
        state.shapeshiftFormID == 29
    self:RefreshPlayerValidity()
end

function Shingen:SetPlayerCastingSpell(spellId)
    local info = spellsList[spellId]
    local castingSpell = info and info.index or 0
    state.castingSpell = castingSpell / 255 or 0
    state.castingSpellRank = info and (info.Rank or 0) / 255 or 0
    self:UpdateStateBlock("状态", "施法目标")
    self:UpdateStateBlock("状态", "施法技能")
    self:UpdateStateBlock("状态", "技能等级")
end

function Shingen:RefreshPlayerConfigStateBlocks()
    if not (self.db and self.db.char) then return end
    local names = { "爆发开关", "AOE开关", "输出模式", "爆发药水开关" }
    for i = 1, #names do
        self:UpdateBareStateBlock(names[i], { "配置开关", "状态" })
    end
end

function Shingen:UpdateRune()
    local total = 0
    for i = 1, 6 do
        local runeCount = GetRuneCount(i)
        if runeCount then
            total = total + runeCount
        end
    end
    state.runeCount = total / 255 or 0
    self:UpdateBareStateBlock("符文", { "能量", "特殊", "状态" })
end

function Shingen:RefreshShapeshiftFormState()
    local shapeshiftFormID = GetShapeshiftFormID() or 0
    state.shapeshiftFormID = shapeshiftFormID / 255
    self:UpdateStateBlock("特殊", "姿态")
end

function Shingen:RefreshDrinkStatus(spellID)
    local name = C_Spell.GetSpellName(spellID)
    if name == "饮水" or name == "进食饮水" then
        state.drinkStatus = true
        self:RefreshPlayerValidity()
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        drinkStatusTimer = C_Timer.NewTimer(20, function()
            state.drinkStatus = false
            self:RefreshPlayerValidity()
            drinkStatusTimer = nil
        end)
    else
        if drinkStatusTimer then
            drinkStatusTimer:Cancel()
            drinkStatusTimer = nil
        end
        state.drinkStatus = false
        self:RefreshPlayerValidity()
    end
end

function Shingen:HookChatFrameEditBox()
    for i = 1, NUM_CHAT_WINDOWS do
        local editBox = _G["ChatFrame" .. i .. "EditBox"]
        if editBox then
            editBox:HookScript("OnEditFocusGained", function()
                state.isChatOpen = true
                self:RefreshPlayerValidity()
            end)
            editBox:HookScript("OnEditFocusLost", function()
                state.isChatOpen = false
                self:RefreshPlayerValidity()
            end)
        end
    end
end

local mounts = {}
local mountCastingTimer = nil

function Shingen:CacheCollectedMountSpells()
    wipe(mounts)
    local mountIDs = C_MountJournal.GetMountIDs()
    for i = 1, #mountIDs do
        local _, spellID, _, _, _, _, _, _, _, _, isCollected = C_MountJournal.GetMountInfoByID(mountIDs[i])
        if isCollected and spellID then
            mounts[spellID] = true
        end
    end
end

function Shingen:SetMountSpellCasting(spellID, isCasting)
    if isCasting then
        if spellID and not issecretvalue(spellID) and mounts[spellID] then
            if mountCastingTimer then
                mountCastingTimer:Cancel()
                mountCastingTimer = nil
            end
            state.mountCasting = true
            self:RefreshPlayerValidity()
        end
    elseif state.mountCasting then
        if mountCastingTimer then
            mountCastingTimer:Cancel()
        end
        mountCastingTimer = C_Timer.NewTimer(0.1, function()
            state.mountCasting = false
            self:RefreshPlayerValidity()
            mountCastingTimer = nil
        end)
    end
end

function Shingen:PreviousSkill(spellId)
    local spellIndex = spellsList[spellId] and spellsList[spellId].index or 0
    state.PreviousSkill = spellIndex / 255 or 0
    self:UpdateStateBlock("状态", "上个技能")
end

-- ============================ 职业特殊状态 ============================

function Shingen:UpdatePlayerStagger()
    local unit = "player"
    local damage = UnitStagger(unit)
    local maxHealth = UnitHealthMax(unit)
    if issecretvalue(damage) or issecretvalue(maxHealth) then
        state.staggerPercent = 0
        self:UpdateStateBlock("特殊", "酒池")
        return
    end
    local staggerPercent = damage / maxHealth * 100
    state.staggerPercent = staggerPercent / 255 or 0
    self:UpdateStateBlock("特殊", "酒池")
end

local forbearanceTimer = nil

function Shingen:UpdatePlayerForbearance() -- 25771 自律
    if forbearanceTimer then
        forbearanceTimer:Cancel()
        forbearanceTimer = nil
    end

    local remaining = 30
    state.forbearance = remaining / 255
    self:UpdateStateBlock("特殊", "自律")

    forbearanceTimer = C_Timer.NewTicker(1, function()
        remaining = remaining - 1
        state.forbearance = remaining > 0 and (remaining / 255) or 0
        self:UpdateStateBlock("特殊", "自律")
        if remaining <= 0 then
            forbearanceTimer = nil
        end
    end, 30)
end
