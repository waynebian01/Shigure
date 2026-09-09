local addon, ns = ...

local isSec = issecretvalue

local state = Fuyutsui.state
local nameplate = Fuyutsui.nameplate

function Fuyutsui:RefreshZoneState()
    state.mapID = C_Map.GetBestMapForUnit("player") or 0
    state.mapInfo = C_Map.GetMapInfo(state.mapID)
    state.subzone = GetSubZoneText()
    if GetBindLocation() == state.subzone then
        print("欢迎回家!")
    end
end

function Fuyutsui:ZONE_CHANGED()
    self:RefreshZoneState()
end

function Fuyutsui:ZONE_CHANGED_INDOORS()
    self:RefreshZoneState()
end

function Fuyutsui:PLAYER_ENTERING_WORLD()
    state.mapID = C_Map.GetBestMapForUnit("player") or 0
    self:UpdateHolyArmaments(375576)
    self:UpdateVampiricStrike(206930)
    self:UpdateReaverGlaive(204157)
    self:UpdateHeroTalent()
    self:CacheCollectedMountSpells()
    self:RefreshChargedComboPoints()
    C_Timer.After(3, function()
        self:RebuildGroupRoster()
        self:LoadPlayerMacros()
    end)
end

function Fuyutsui:PLAYER_TALENT_UPDATE()
    self:RebuildSpecializationState()
    self:RebuildGroupRoster()
    self:RefreshChargedComboPoints()
end

function Fuyutsui:RefreshPlayerDeathAndValidity()
    self.state.isDead = UnitIsDeadOrGhost("player")
    self:RefreshPlayerValidity()
end

function Fuyutsui:PLAYER_DEAD()
    self:RefreshPlayerDeathAndValidity()
end

function Fuyutsui:PLAYER_ALIVE()
    self:RefreshPlayerDeathAndValidity()
end

function Fuyutsui:PLAYER_UNGHOST()
    self:RefreshPlayerDeathAndValidity()
end

function Fuyutsui:PLAYER_MOUNT_DISPLAY_CHANGED()
    self:RefreshPlayerMountedState()
end

function Fuyutsui:UNIT_PET(_, unit)
    if unit == "player" then
        self:RefreshPlayerPetState()
        self:UpdateSpellKnown()
    end
end

function Fuyutsui:PLAYER_REGEN_DISABLED()
    self:RefreshTargetReactionState()
    state.combat = true
    state.combatStartTime = GetTime()
    self:RefreshPlayerCombatDuration()
end

function Fuyutsui:PLAYER_REGEN_ENABLED()
    self:RefreshTargetReactionState()
    state.combat = false
    self:RefreshPlayerCombatDuration()
end

function Fuyutsui:PLAYER_STARTED_MOVING()
    self:SetPlayerMoving(true)
end

function Fuyutsui:PLAYER_STOPPED_MOVING()
    self:SetPlayerMoving(false)
end

function Fuyutsui:UNIT_SPELLCAST_SENT(_, unitTarget, targetName, castGUID, spellID)
    if unitTarget ~= "player" then return end
    if not isSec(targetName) then
        for unit, data in pairs(self.group) do
            if data.name == targetName then
                state.castTargetUnit = unit
                state.castTargetName = targetName
                state.castTargetIndex = data.index / 255
                break
            end
        end
    end
end

local function SetUnitCastState(self, unit, stateField, isActive)
    if not self:SetTrackedUnitCastState(unit, stateField, isActive) then return false end
    if isActive then return true end

    if unit == "player" then
        if stateField == "casting" then
            self:RefreshPlayerCastingStateBlocks()
        elseif stateField == "channeling" then
            self:RefreshPlayerChannelStateBlock()
        elseif stateField == "empowering" then
            self:RefreshPlayerEmpowerStateBlocks()
        end
    else
        self:ClearUnitCastStateBlocks(unit, stateField)
    end
    return true
end

local function ClearPlayerCastTarget(self)
    state.castTargetUnit = nil
    state.castTargetName = nil
    state.castTargetIndex = 0
    self:SetPlayerCastingSpell(0)
end

function Fuyutsui:UNIT_SPELLCAST_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", true)
    if unitTarget == "player" then
        self:RecordIncomingHealEstimate(spellID)
        self:SetPlayerCastingSpell(spellID)
        self:SetMountSpellCasting(spellID, true)
    end
end

function Fuyutsui:UNIT_SPELLCAST_STOP(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", false)
    if unitTarget == "player" then
        self:ClearIncomingHealEstimates()
        ClearPlayerCastTarget(self)
        self:SetMountSpellCasting(spellID, false)
    end
end

function Fuyutsui:UNIT_SPELLCAST_INTERRUPTED(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", false)
    if unitTarget == "player" then
        self:ClearIncomingHealEstimates()
        ClearPlayerCastTarget(self)
        self:SetMountSpellCasting(spellID, false)
    end
end

function Fuyutsui:UNIT_SPELLCAST_CHANNEL_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "channeling", true)
    if unitTarget == "player" then
        state.channelingSpellID = spellID
        self:SetPlayerCastingSpell(spellID)
    end
end

function Fuyutsui:UNIT_SPELLCAST_CHANNEL_STOP(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "channeling", false)
    if unitTarget == "player" then
        state.channelingSpellID = nil
        ClearPlayerCastTarget(self)
    end
end

function Fuyutsui:UNIT_SPELLCAST_EMPOWER_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "empowering", true)
    if unitTarget == "player" then
        state.empoweringSpellID = spellID
        self:SetPlayerCastingSpell(spellID)
    end
end

function Fuyutsui:UNIT_SPELLCAST_EMPOWER_STOP(_, unitTarget, castGUID, spellID, complete, interruptedBy, castBarID)
    SetUnitCastState(self, unitTarget, "empowering", false)
    if unitTarget == "player" then
        state.empoweringSpellID = nil
        ClearPlayerCastTarget(self)
    end
end

function Fuyutsui:UNIT_SPELLCAST_SUCCEEDED(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget ~= "player" or isSec(spellID) then return end
    self:RefreshDrinkStatus(spellID)
    self:UpdateInsertSpellBySuccess(spellID)
    self:UpdateInsertItemBySuccess(spellID)
    self:PreviousSkill(spellID)
    self:UpdateActiveTotemRemainingTime(spellID)
    if spellID == 384255 then
        self:ClearAllFuyutsuiBars()
        print("切换天赋")
        C_Timer.After(1, function()
            self:RebuildSpecializationState()
        end)
    elseif spellID == 200749 then
        self:ClearAllFuyutsuiBars()
        print("切换专精")
        C_Timer.After(1, function()
            self:RebuildSpecializationState()
        end)
    end
end

function Fuyutsui:SPELL_UPDATE_COOLDOWN(_, spellID, baseSpellID)
    if issecretvalue(spellID) then return end
    -- print(spellID, baseSpellID, C_Spell.GetSpellLink(spellID))
    if spellID == 25771 then
        self:UpdatePlayerForbearance()
    end
    self:RecordKnightSpellState(spellID)
    self:UpdateBoilingPoint(spellID) -- 沸点
end

local potions = {
    [241304] = "银月城生命药水",
    [241305] = "银月城生命药水",
    [271884] = "浓缩银月城生命药水",
    [271885] = "浓缩银月城生命药水",
    [5512] = "治疗石",
    [224464] = "恶魔治疗石",
    [241301] = "光注法力药水",
    [241300] = "光注法力药水",
    [241288] = "鲁莽药水",
    [241289] = "鲁莽药水",
    [241308] = "圣光潜力",
    [241309] = "圣光潜力",
    [241292] = "狂放恣意饮剂",
    [241293] = "狂放恣意饮剂",
}

function Fuyutsui:ITEM_COUNT_CHANGED()
    self:UpdateItemCooldown()
end

function Fuyutsui:PLAYERBANKSLOTS_CHANGED()
    self:UpdateItemCooldown()
end

function Fuyutsui:BAG_UPDATE()
    self:UpdateItemCooldown()
end

function Fuyutsui:UNIT_HEALTH(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
        self:UpdatePlayerStagger()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
    if unit == "target" then
        self:RefreshTargetHealthState()
    end
    if unit == "focus" then
        self:RefreshFocusHealthState()
    end
    if unit == "mouseover" then
        self:RefreshMouseoverHealthState()
    end
    if unit == "pet" then
        self:RefreshUnitHealthState(unit)
    end
    if self:IsBossUnit(unit) then
        self:RefreshUnitHealthState(unit)
    end
end

function Fuyutsui:UNIT_MAXHEALTH(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
    if unit == "mouseover" then
        self:RefreshMouseoverHealthState()
    end
    if unit == "pet" then
        self:RefreshUnitHealthState(unit)
    end
    if self:IsBossUnit(unit) then
        self:RefreshUnitHealthState(unit)
    end
end

function Fuyutsui:UNIT_HEAL_ABSORB_AMOUNT_CHANGED(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_HEAL_PREDICTION(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_POWER_UPDATE(_, unit, powerType)
    if unit ~= "player" then return end
    self:UpdatePlayerPower(powerType)
    if powerType == "COMBO_POINTS" then
        C_Timer.After(0, function()
            self:RefreshChargedComboPoints()
        end)
    end
end

function Fuyutsui:UNIT_POWER_POINT_CHARGE(_, unit)
    if unit ~= "player" then return end
    C_Timer.After(0, function()
        self:RefreshChargedComboPoints()
    end)
end

function Fuyutsui:SPELL_UPDATE_USES(_, spellID, baseSpellID)
end

function Fuyutsui:SPELL_UPDATE_ICON(_, spellID)
    if issecretvalue(spellID) then return end
    self:UpdateHolyArmaments(spellID)
    self:UpdateVampiricStrike(spellID)
    self:UpdateReaverGlaive(spellID)
    self:UpdateHeroicStrike(spellID)
end

local rosterTimer
function Fuyutsui:GROUP_ROSTER_UPDATE()
    state.castTargetName, state.castTargetUnit = nil, nil
    if rosterTimer then
        rosterTimer:Cancel()
    end
    rosterTimer = C_Timer.NewTimer(1, function()
        self:RebuildGroupRoster()
        self:RefreshGroupCountState()
        self:RefreshGroupTypeState()
        rosterTimer = nil
    end)
end

function Fuyutsui:UNIT_DIED(_, unitGUID)
    if not isSec(unitGUID) then
        self:RefreshGroupMemberDeath(unitGUID, "guid")
    end
end

function Fuyutsui:SPELL_RANGE_CHECK_UPDATE()
end

function Fuyutsui:ACTION_RANGE_CHECK_UPDATE(_, slot, isInRange, checksRange)
end

function Fuyutsui:UI_ERROR_MESSAGE(_, errorType, message)
    if message == "目标不在视野中" then
        self:MarkGroupMemberTemporarilyOutOfSight(state.castTargetUnit)
    end
end

function Fuyutsui:UPDATE_BINDINGS()
    self:ReadKeybindings()
end

function Fuyutsui:SPELLS_CHANGED()
    self:ReadKeybindings()
end

function Fuyutsui:ACTIONBAR_SHOWGRID()
    self:ReadKeybindings()
end

function Fuyutsui:ACTIONBAR_HIDEGRID()
    self:ReadKeybindings()
end

function Fuyutsui:PLAYER_TARGET_CHANGED()
    self:RefreshTargetState()
    self:UpdateUnitAuraContainer("target")
end

function Fuyutsui:PLAYER_FOCUS_CHANGED()
    self:RefreshFocusState()
    self:UpdateUnitAuraContainer("focus")
end

function Fuyutsui:UPDATE_MOUSEOVER_UNIT()
    self:RefreshMouseoverState()
end

--- 过场/影片结束后重绑 spellId 过滤（槽位否则会落到排序第一的光环）
function Fuyutsui:CINEMATIC_STOP()
    C_Timer.After(1, function()
        self:RebindAuraSpellFilters()
    end)
end

function Fuyutsui:STOP_MOVIE()
    C_Timer.After(1, function()
        self:RebindAuraSpellFilters()
    end)
end

function Fuyutsui:NAME_PLATE_UNIT_ADDED(_, unit)
    self:CacheNameplateUnit(unit)
    if self.RefreshNameplatePixels then
        self:RefreshNameplatePixels()
    end
    self:RefreshTargetReactionState()
    self:RefreshBossReactionAndRangeStates()
end

function Fuyutsui:NAME_PLATE_UNIT_REMOVED(_, unit)
    nameplate[unit] = nil
    if self.ClearNameplatePixelSlot then
        self:ClearNameplatePixelSlot(unit)
    end
    self:RefreshTargetReactionState()
end

function Fuyutsui:UNIT_THREAT_SITUATION_UPDATE(_, unitTarget)
    if nameplate[unitTarget] then
        self:RefreshNameplateThreat(unitTarget)
        self:RefreshThreatEnemyCounts()
        return
    end
    if unitTarget ~= "player" then return end
    for unit in pairs(nameplate) do
        self:RefreshNameplateThreat(unit)
    end
    self:RefreshThreatEnemyCounts()
end

function Fuyutsui:RefreshShapeshiftAndMountStates()
    self:RefreshShapeshiftFormState()
    self:RefreshPlayerMountedState()
end

function Fuyutsui:UPDATE_SHAPESHIFT_FORM()
    self:RefreshShapeshiftAndMountStates()
end

function Fuyutsui:UPDATE_SHAPESHIFT_FORMS()
    self:RefreshShapeshiftAndMountStates()
end

function Fuyutsui:ENCOUNTER_START(_, encounterID, encounterName, difficultyID, groupSize)
    self:SetEncounterState(encounterID, difficultyID)
end

function Fuyutsui:ENCOUNTER_END(_, encounterID, encounterName, difficultyID, groupSize, success)
    self:SetEncounterState(0, 0)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_ADDED(_, eventInfo)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_REMOVED(_, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED(_, eventID)
end

function Fuyutsui:StartFrameUpdates()
    if not self.updateFrame then
        self.updateFrame = CreateFrame("Frame")
    end
    local parent = self
    self.updateFrame:SetScript("OnUpdate", function(frame, elapsed)
        parent:OnUpdate(elapsed)
    end)
end

Fuyutsui.timeElapsed = 0
Fuyutsui.timeElapsed1 = 0

local updateErrorTimes = {}
local UPDATE_ERROR_THROTTLE_SECONDS = 10

local function RunUpdateSafely(self, methodName, ...)
    local method = self[methodName]
    if type(method) ~= "function" then return end

    local errorKey = methodName
    local context = select(1, ...)
    if type(context) == "string" then
        errorKey = methodName .. "(" .. context .. ")"
    end

    local success, errorMessage = pcall(method, self, ...)
    if success then return end

    local now = GetTime()
    local lastErrorTime = updateErrorTimes[errorKey]
    if not lastErrorTime or now - lastErrorTime >= UPDATE_ERROR_THROTTLE_SECONDS then
        updateErrorTimes[errorKey] = now
        print("Fuyutsui OnUpdate error [" .. errorKey .. "]: " .. tostring(errorMessage))
    end
end

function Fuyutsui:OnUpdate(elapsed)
    RunUpdateSafely(self, "RefreshNextGroupMemberState")
    RunUpdateSafely(self, "UpdateStateBlock", "状态", "公共冷却")

    self.timeElapsed = self.timeElapsed + elapsed
    if self.timeElapsed > 0.2 then
        RunUpdateSafely(self, "UpdateSpellCooldown")
        RunUpdateSafely(self, "RefreshAssistedCombatSuggestion")
        RunUpdateSafely(self, "UpdateRune")
        RunUpdateSafely(self, "RefreshTargetRangeState")
        RunUpdateSafely(self, "RefreshFocusRangeState")
        RunUpdateSafely(self, "RefreshMouseoverRangeState")

        RunUpdateSafely(self, "RefreshEnemyCounts")
        RunUpdateSafely(self, "RefreshNameplatePixels")
        RunUpdateSafely(self, "UpdateItemCooldown")
        self.timeElapsed = 0
    end

    self.timeElapsed1 = self.timeElapsed1 + elapsed
    if self.timeElapsed1 >= 1 then
        RunUpdateSafely(self, "RefreshPlayerCombatDuration")
        RunUpdateSafely(self, "RefreshActiveKnightCount")
        self.timeElapsed1 = 0
    end

    RunUpdateSafely(self, "RefreshPlayerCastStateBlocks")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "target")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "focus")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "mouseover")
    RunUpdateSafely(self, "RefreshBossCastStateBlocks", RunUpdateSafely)
end
