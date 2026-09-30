local addon, ns = ...

local isSec = issecretvalue

local state = Shingen.state
local playerCountdownTicker = nil
local playerCountdownRemaining = 0
local COMBAT_UNIT_REFRESH_ORDER = {
    "target",
    "focus",
    "mouseover",
    "boss1",
    "boss2",
    "boss3",
    "boss4",
    "boss5",
}

local function SyncPlayerCountdownPixel()
    state.playerCountdown = math.min(255, playerCountdownRemaining) / 255
    Shingen:UpdateStateBlock("特殊", "倒数")
end

function Shingen:StopPlayerCountdown()
    if playerCountdownTicker then
        playerCountdownTicker:Cancel()
        playerCountdownTicker = nil
    end
    playerCountdownRemaining = 0
    SyncPlayerCountdownPixel()
end

function Shingen:StartPlayerCountdown(timeRemaining)
    if playerCountdownTicker then
        playerCountdownTicker:Cancel()
        playerCountdownTicker = nil
    end

    if isSec(timeRemaining) then
        playerCountdownRemaining = 0
        SyncPlayerCountdownPixel()
        return
    end

    playerCountdownRemaining = math.max(0, math.ceil(tonumber(timeRemaining) or 0))
    SyncPlayerCountdownPixel()
    if playerCountdownRemaining == 0 then return end

    playerCountdownTicker = C_Timer.NewTicker(1, function()
        playerCountdownRemaining = math.max(0, playerCountdownRemaining - 1)
        SyncPlayerCountdownPixel()
        if playerCountdownRemaining == 0 then
            playerCountdownTicker:Cancel()
            playerCountdownTicker = nil
        end
    end)
end

function Shingen:START_PLAYER_COUNTDOWN(_, initiatedBy, timeRemaining, totalTime, informChat, initiatedByName)
    self:StartPlayerCountdown(timeRemaining)
end

function Shingen:CANCEL_PLAYER_COUNTDOWN(_, initiatedBy, informChat, initiatedByName)
    self:StopPlayerCountdown()
end

function Shingen:RefreshZoneState()
    state.mapID = C_Map.GetBestMapForUnit("player") or 0
    state.mapInfo = C_Map.GetMapInfo(state.mapID)
    state.subzone = GetSubZoneText()
    if GetBindLocation() == state.subzone then
        print("欢迎回家!")
    end
end

function Shingen:ZONE_CHANGED()
    self:RefreshZoneState()
end

function Shingen:ZONE_CHANGED_INDOORS()
    self:RefreshZoneState()
end

function Shingen:PLAYER_ENTERING_WORLD()
    state.mapID = C_Map.GetBestMapForUnit("player") or 0
    self:CacheCollectedMountSpells()
    self:RefreshChargedComboPoints()
    C_Timer.After(2, function()
        self:RefreshAllPlayerPowers()
        self:RebuildGroupRoster()
        self:LoadPlayerMacros()
    end)
end

function Shingen:PLAYER_TALENT_UPDATE()
    self:RebuildSpecializationState()
    self:RebuildGroupRoster()
    self:RefreshChargedComboPoints()
end

function Shingen:RefreshPlayerDeathAndValidity()
    self.state.isDead = UnitIsDeadOrGhost("player")
    self:RefreshPlayerValidity()
end

function Shingen:PLAYER_DEAD()
    self:RefreshPlayerDeathAndValidity()
end

function Shingen:PLAYER_ALIVE()
    self:RefreshPlayerDeathAndValidity()
end

function Shingen:PLAYER_UNGHOST()
    self:RefreshPlayerDeathAndValidity()
end

function Shingen:PLAYER_MOUNT_DISPLAY_CHANGED()
    self:RefreshPlayerMountedState()
end

function Shingen:UNIT_PET(_, unit)
    if unit == "player" then
        self:RefreshPlayerPetState()
        self:UpdateSpellKnown()
    end
end

--- 进入战斗时立即重写所有可作为目标的单位像素，避免等待后续事件或 0.2 秒轮询。
--- 这里不调用 RefreshUnitState，防止仅因进入战斗而清空正在进行的施法缓存。
function Shingen:RefreshCombatUnitPixels()
    for _, unit in ipairs(COMBAT_UNIT_REFRESH_ORDER) do
        self:RefreshUnitReactionState(unit)
        self:RefreshUnitDeathState(unit)
        self:RefreshUnitHealthState(unit)
        self:RefreshUnitPowerState(unit)
        self:RefreshUnitRangeState(unit)
    end

    self:UpdateUnitAuraContainer("target")
    self:UpdateUnitAuraContainer("focus")

    if self.RefreshNameplatePixels then
        self:RefreshNameplatePixels()
    end
end

function Shingen:PLAYER_REGEN_DISABLED()
    state.combat = true
    state.combatStartTime = GetTime()
    self:RefreshPlayerCombatDuration()
    self:RefreshCombatUnitPixels()
end

function Shingen:PLAYER_REGEN_ENABLED()
    self:RefreshTargetReactionState()
    state.combat = false
    self:RefreshPlayerCombatDuration()
end

function Shingen:PLAYER_STARTED_MOVING()
    self:SetPlayerMoving(true)
end

function Shingen:PLAYER_STOPPED_MOVING()
    self:SetPlayerMoving(false)
end

function Shingen:UNIT_SPELLCAST_SENT(_, unitTarget, targetName, castGUID, spellID)
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

function Shingen:UNIT_SPELLCAST_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", true)
    if unitTarget == "player" then
        self:RecordIncomingHealEstimate(spellID)
        self:SetPlayerCastingSpell(spellID)
        self:SetMountSpellCasting(spellID, true)
    end
end

function Shingen:UNIT_SPELLCAST_STOP(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", false)
    if unitTarget == "player" then
        self:ClearIncomingHealEstimates()
        ClearPlayerCastTarget(self)
        self:SetMountSpellCasting(spellID, false)
    end
end

function Shingen:UNIT_SPELLCAST_INTERRUPTED(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "casting", false)
    if unitTarget == "player" then
        self:ClearIncomingHealEstimates()
        ClearPlayerCastTarget(self)
        self:SetMountSpellCasting(spellID, false)
    end
end

function Shingen:UNIT_SPELLCAST_CHANNEL_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "channeling", true)
    if unitTarget == "player" then
        state.channelingSpellID = spellID
        self:SetPlayerCastingSpell(spellID)
    end
end

function Shingen:UNIT_SPELLCAST_CHANNEL_STOP(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "channeling", false)
    if unitTarget == "player" then
        state.channelingSpellID = nil
        ClearPlayerCastTarget(self)
    end
end

function Shingen:UNIT_SPELLCAST_EMPOWER_START(_, unitTarget, castGUID, spellID, castBarID)
    SetUnitCastState(self, unitTarget, "empowering", true)
    if unitTarget == "player" then
        state.empoweringSpellID = spellID
        self:SetPlayerCastingSpell(spellID)
    end
end

function Shingen:UNIT_SPELLCAST_EMPOWER_STOP(_, unitTarget, castGUID, spellID, complete, interruptedBy, castBarID)
    SetUnitCastState(self, unitTarget, "empowering", false)
    if unitTarget == "player" then
        state.empoweringSpellID = nil
        ClearPlayerCastTarget(self)
    end
end

function Shingen:UNIT_SPELLCAST_SUCCEEDED(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget ~= "player" or isSec(spellID) then return end
    self:RefreshDrinkStatus(spellID)
    self:UpdateInsertSpellBySuccess(spellID)
    self:UpdateInsertItemBySuccess(spellID)
    self:PreviousSkill(spellID)
end

function Shingen:SPELL_UPDATE_COOLDOWN(_, spellID, baseSpellID)
    if issecretvalue(spellID) then return end
    -- print(spellID, baseSpellID, C_Spell.GetSpellLink(spellID))
    if spellID == 25771 then
        self:UpdatePlayerForbearance()
    end
end

function Shingen:ITEM_COUNT_CHANGED()
    self:UpdateItemCooldown()
end

function Shingen:PLAYERBANKSLOTS_CHANGED()
    self:UpdateItemCooldown()
end

function Shingen:BAG_UPDATE()
    self:UpdateItemCooldown()
end

function Shingen:UNIT_HEALTH(_, unit)
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

function Shingen:UNIT_MAXHEALTH(_, unit)
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

function Shingen:UNIT_HEAL_ABSORB_AMOUNT_CHANGED(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
end

function Shingen:UNIT_HEAL_PREDICTION(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:RefreshGroupMemberHealth(unit)
        self:RefreshGroupMemberDeath(unit, "health")
    end
end

function Shingen:UNIT_POWER_UPDATE(_, unit, powerType)
    if unit == "player" then
        self:UpdatePlayerPower(powerType)
        if powerType == "COMBO_POINTS" then
            C_Timer.After(0, function()
                self:RefreshChargedComboPoints()
            end)
        end
        return
    end

    self:RefreshUnitPowerState(unit)
end

function Shingen:UNIT_MAXPOWER(_, unit)
    self:RefreshUnitPowerState(unit)
end

function Shingen:UNIT_POWER_POINT_CHARGE(_, unit)
    if unit ~= "player" then return end
    C_Timer.After(0, function()
        self:RefreshChargedComboPoints()
    end)
end

function Shingen:SPELL_UPDATE_USES(_, spellID, baseSpellID)
end

function Shingen:SPELL_UPDATE_ICON(_, spellID)
    if issecretvalue(spellID) then return end
end

local rosterTimer
function Shingen:GROUP_ROSTER_UPDATE()
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

function Shingen:UNIT_DIED(_, unitGUID)
    if not isSec(unitGUID) then
        self:RefreshGroupMemberDeath(unitGUID, "guid")
    end
end

function Shingen:SPELL_RANGE_CHECK_UPDATE()
end

function Shingen:ACTION_RANGE_CHECK_UPDATE(_, slot, isInRange, checksRange)
end

function Shingen:UI_ERROR_MESSAGE(_, errorType, message)
    if message == "目标不在视野中" then
        self:MarkGroupMemberTemporarilyOutOfSight(state.castTargetUnit)
    end
end

function Shingen:UPDATE_BINDINGS()
    self:ReadKeybindings()
end

function Shingen:SPELLS_CHANGED()
    self:ReadKeybindings()
end

function Shingen:ACTIONBAR_SHOWGRID()
    self:ReadKeybindings()
end

function Shingen:ACTIONBAR_HIDEGRID()
    self:ReadKeybindings()
end

function Shingen:PLAYER_TARGET_CHANGED()
    self:RefreshTargetState()
    self:UpdateUnitAuraContainer("target")
    if self.RefreshNameplateUnitMappings then
        self:RefreshNameplateUnitMappings()
    end
end

function Shingen:PLAYER_FOCUS_CHANGED()
    self:RefreshFocusState()
    self:UpdateUnitAuraContainer("focus")
    if self.RefreshNameplateUnitMappings then
        self:RefreshNameplateUnitMappings()
    end
end

function Shingen:UPDATE_MOUSEOVER_UNIT()
    self:RefreshMouseoverState()
end

--- 过场/影片结束后重绑 spellId 过滤（槽位否则会落到排序第一的光环）
function Shingen:CINEMATIC_STOP()
    C_Timer.After(1, function()
        self:RebindAuraSpellFilters()
    end)
end

function Shingen:STOP_MOVIE()
    C_Timer.After(1, function()
        self:RebindAuraSpellFilters()
    end)
end

function Shingen:NAME_PLATE_UNIT_ADDED(_, unit)
    if self.RefreshNameplatePixels then
        self:RefreshNameplatePixels()
    end
    self:RefreshTargetReactionState()
    self:RefreshBossReactionAndRangeStates()
end

function Shingen:NAME_PLATE_UNIT_REMOVED(_, unit)
    if self.ClearNameplatePixelSlot then
        self:ClearNameplatePixelSlot(unit)
    end
    if self.RefreshNameplateUnitMappings then
        self:RefreshNameplateUnitMappings()
    end
    self:RefreshTargetReactionState()
end

function Shingen:UNIT_THREAT_SITUATION_UPDATE(_, unitTarget)
    -- 玩家或姓名板仇恨变化时立即更新；常规轮询继续负责刷新全部槽位。
    if unitTarget == "player" or (type(unitTarget) == "string" and unitTarget:match("^nameplate%d+$")) then
        self:RefreshNameplatePixels()
    end
end

function Shingen:RefreshShapeshiftAndMountStates()
    self:RefreshShapeshiftFormState()
    self:RefreshPlayerMountedState()
end

function Shingen:UPDATE_SHAPESHIFT_FORM()
    self:RefreshShapeshiftAndMountStates()
end

function Shingen:UPDATE_SHAPESHIFT_FORMS()
    self:RefreshShapeshiftAndMountStates()
end

function Shingen:ENCOUNTER_START(_, encounterID, encounterName, difficultyID, groupSize)
    if self.ResetExBossTimelinePixels then
        self:ResetExBossTimelinePixels()
    end
    self:SetEncounterState(encounterID, difficultyID)
    self:RefreshBossAuraContainers()
end

function Shingen:ENCOUNTER_END(_, encounterID, encounterName, difficultyID, groupSize, success)
    if self.ResetExBossTimelinePixels then
        self:ResetExBossTimelinePixels()
    end
    if self.ResetBigWigsTimelinePixels then
        self:ResetBigWigsTimelinePixels()
    end
    self:SetEncounterState(0, 0)
    self:RefreshBossAuraContainers()
end

function Shingen:INSTANCE_ENCOUNTER_ENGAGE_UNIT()
    self:RefreshBossUnitStates()
    self:RefreshBossAuraContainers()
end

function Shingen:ENCOUNTER_TIMELINE_EVENT_ADDED(_, eventInfo)
end

function Shingen:ENCOUNTER_TIMELINE_EVENT_REMOVED(_, eventID)
end

function Shingen:ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED(_, eventID)
end

function Shingen:StartFrameUpdates()
    if not self.updateFrame then
        self.updateFrame = CreateFrame("Frame")
    end
    local parent = self
    self.updateFrame:SetScript("OnUpdate", function(frame, elapsed)
        parent:OnUpdate(elapsed)
    end)
end

Shingen.timeElapsed = 0
Shingen.timeElapsed1 = 0

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
        print("Shingen OnUpdate error [" .. errorKey .. "]: " .. tostring(errorMessage))
    end
end

function Shingen:OnUpdate(elapsed)
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

        RunUpdateSafely(self, "RefreshNameplatePixels")
        RunUpdateSafely(self, "UpdateItemCooldown")
        RunUpdateSafely(self, "RefreshExBossTimelinePixels")
        RunUpdateSafely(self, "RefreshExTrashTimelinePixels")
        RunUpdateSafely(self, "RefreshBigWigsTimelinePixels")
        self.timeElapsed = 0
    end

    self.timeElapsed1 = self.timeElapsed1 + elapsed
    if self.timeElapsed1 >= 1 then
        RunUpdateSafely(self, "RefreshPlayerCombatDuration")
        self.timeElapsed1 = 0
    end

    RunUpdateSafely(self, "RefreshPlayerCastStateBlocks")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "target")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "focus")
    RunUpdateSafely(self, "RefreshUnitCastStateBlocks", "mouseover")
    RunUpdateSafely(self, "RefreshBossCastStateBlocks", RunUpdateSafely)
end
