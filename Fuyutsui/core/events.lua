local addon, ns = ...

local isSec = issecretvalue

local state = Fuyutsui.state
local target = Fuyutsui.target
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
    self:UpdateReaverGlaive(204157)
    self:UpdateVampiricStrike(55090, C_Spell.GetOverrideSpell(55090))
    self:UpdateHeroTalent()
    self:GetMountsInfo()
    self:UpdateChargedComboPoints()
    C_Timer.After(3, function()
        self:UpdateGroup()
        self:LoadPlayerMacros()
    end)
end

function Fuyutsui:PLAYER_TALENT_UPDATE()
    self:UpdatePlayerSpecInfo()
    self:UpdateGroup()
    self:UpdateChargedComboPoints()
end

function Fuyutsui:RefreshPlayerDeathValid()
    self.state.isDead = UnitIsDeadOrGhost("player")
    self:UpdatePlayerValid()
end

function Fuyutsui:PLAYER_DEAD()
    self:RefreshPlayerDeathValid()
end

function Fuyutsui:PLAYER_ALIVE()
    self:RefreshPlayerDeathValid()
end

function Fuyutsui:PLAYER_UNGHOST()
    self:RefreshPlayerDeathValid()
end

function Fuyutsui:PLAYER_MOUNT_DISPLAY_CHANGED()
    self:UpdatePlayerMounted()
end

function Fuyutsui:UNIT_PET(_, unit)
    if unit == "player" then
        self:UpdatePlayerPet()
    end
end

function Fuyutsui:PLAYER_REGEN_DISABLED()
    self:UpdateTargetCanAttack()
    state.combat = true
    state.combatStartTime = GetTime()
end

function Fuyutsui:PLAYER_REGEN_ENABLED()
    self:UpdateTargetCanAttack()
    state.combat = false
end

function Fuyutsui:PLAYER_STARTED_MOVING()
    self:UpdatePlayerMoving(true)
end

function Fuyutsui:PLAYER_STOPPED_MOVING()
    self:UpdatePlayerMoving(false)
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

function Fuyutsui:UNIT_SPELLCAST_START(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        state.casting = true
        self:ApplyIncomingHealsCurve(spellID)
        self:UpdatePlayerCasting(spellID)
        self:UpdateMountCasting(spellID, true)
    end
    if unitTarget == "target" then
        target.casting = true
    end
end

function Fuyutsui:UNIT_SPELLCAST_STOP(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        self:UpdateAllIncomingHealsCurves()
        state.casting = false
        state.castTargetUnit = nil
        state.castTargetName = nil
        state.castTargetIndex = 0
        self:UpdatePlayerCasting(0)
        self:UpdateMountCasting(spellID, false)
    elseif unitTarget == "target" then
        target.casting = false
    end
end

function Fuyutsui:UNIT_SPELLCAST_INTERRUPTED(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        self:UpdateMountCasting(spellID, false)
    end
end

function Fuyutsui:UNIT_SPELLCAST_CHANNEL_START(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        state.channeling = true
        state.channelingSpellID = spellID
        self:UpdatePlayerCasting(spellID)
    elseif unitTarget == "target" then
        target.channeling = true
    end
end

function Fuyutsui:UNIT_SPELLCAST_CHANNEL_STOP(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        state.channeling = false
        state.castTargetUnit = nil
        state.castTargetName = nil
        state.castTargetIndex = 0
        self:UpdatePlayerCasting(0)
    elseif unitTarget == "target" then
        target.channeling = false
    end
end

function Fuyutsui:UNIT_SPELLCAST_EMPOWER_START(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget == "player" then
        state.empowering = true
        state.empoweringSpellID = spellID
        self:UpdatePlayerCasting(spellID)
    elseif unitTarget == "target" then
        target.empowering = true
    end
end

function Fuyutsui:UNIT_SPELLCAST_EMPOWER_STOP(_, unitTarget, castGUID, spellID, complete, interruptedBy, castBarID)
    if unitTarget == "player" then
        state.empowering = false
        state.castTargetUnit = nil
        state.castTargetName = nil
        state.castTargetIndex = 0
        self:UpdatePlayerCasting(0)
    elseif unitTarget == "target" then
        target.empowering = false
    end
end

function Fuyutsui:UNIT_SPELLCAST_SUCCEEDED(_, unitTarget, castGUID, spellID, castBarID)
    if unitTarget ~= "player" or isSec(spellID) then return end
    self:UpdateDrinkStatus(spellID)
    self:UpdateInsertSpellBySuccess(spellID)
    if spellID == 384255 then
        self:ClearAllFuyutsuiBars()
        print("切换天赋")
        C_Timer.After(1, function()
            self:UpdatePlayerSpecInfo()
        end)
    elseif spellID == 200749 then
        self:ClearAllFuyutsuiBars()
        print("切换专精")
        C_Timer.After(1, function()
            self:UpdatePlayerSpecInfo()
        end)
    end
end

function Fuyutsui:SPELL_UPDATE_COOLDOWN(_, spellID, baseSpellID)
    if issecretvalue(spellID) then return end
    -- print(spellID, baseSpellID, C_Spell.GetSpellLink(spellID))
    if spellID == 25771 then
        self:UpdatePlayerForbearance()
    end
    self:UpdateKnightStatus(spellID)
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
    self:GetItemCount()
end

function Fuyutsui:PLAYERBANKSLOTS_CHANGED()
    self:GetItemCount()
end

function Fuyutsui:BAG_UPDATE()
    self:GetItemCount()
end

function Fuyutsui:UNIT_HEALTH(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
        self:UpdatePlayerStagger()
    end
    if unit == "target" then
        self:UpdateTargetHealth()
    end
    if unit == "focus" then
        self:UpdateFocusHealth()
    end
    if unit == "mouseover" then
        self:UpdateMouseoverHealth()
    end
    if unit == "pet" then
        self:UpdateUnitHealthBlock(unit)
    end
    if unit and unit:match("^boss[1-5]$") then
        self:UpdateUnitHealthBlock(unit)
    end
    if self.group[unit] then
        self:UpdateUnitDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_MAXHEALTH(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if unit == "mouseover" then
        self:UpdateMouseoverHealth()
    end
    if unit == "pet" then
        self:UpdateUnitHealthBlock(unit)
    end
    if unit and unit:match("^boss[1-5]$") then
        self:UpdateUnitHealthBlock(unit)
    end
    if self.group[unit] then
        self:UpdateUnitDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_HEAL_ABSORB_AMOUNT_CHANGED(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:UpdateUnitDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_HEAL_PREDICTION(_, unit)
    if unit == "player" then
        self:UpdatePlayerHealth()
    end
    if self.group[unit] then
        self:UpdateUnitDeath(unit, "health")
    end
end

function Fuyutsui:UNIT_POWER_UPDATE(_, unit, powerType)
    if unit ~= "player" then return end
    self:UpdatePlayerPower(powerType)
    if powerType == "COMBO_POINTS" then
        C_Timer.After(0, function()
            self:UpdateChargedComboPoints()
        end)
    end
end

function Fuyutsui:UNIT_POWER_POINT_CHARGE(_, unit)
    if unit ~= "player" then return end
    C_Timer.After(0, function()
        self:UpdateChargedComboPoints()
    end)
end

function Fuyutsui:SPELL_UPDATE_USES(_, spellID, baseSpellID)
end

function Fuyutsui:SPELL_UPDATE_ICON(_, spellID)
    if issecretvalue(spellID) then return end
    self:UpdateHolyArmaments(spellID)
    self:UpdateReaverGlaive(spellID)
    self:UpdateHeroicStrike(spellID)
end


function Fuyutsui:COOLDOWN_VIEWER_SPELL_OVERRIDE_UPDATED(_, baseSpellID, overrideSpellID)
    if issecretvalue(overrideSpellID) then return end
    self:UpdateVampiricStrike(baseSpellID, overrideSpellID)
end

local rosterTimer
function Fuyutsui:GROUP_ROSTER_UPDATE()
    state.castTargetName, state.castTargetUnit = nil, nil
    if rosterTimer then
        rosterTimer:Cancel()
    end
    rosterTimer = C_Timer.NewTimer(1, function()
        self:UpdateGroup()
        self:UpdateGroupCount()
        self:UpdateGroupType()
        rosterTimer = nil
    end)
end

function Fuyutsui:UNIT_DIED(_, unitGUID)
    if not isSec(unitGUID) then
        self:UpdateUnitDeath(unitGUID, "guid")
    end
end

function Fuyutsui:SPELL_RANGE_CHECK_UPDATE()
end

function Fuyutsui:ACTION_RANGE_CHECK_UPDATE(_, slot, isInRange, checksRange)
end

function Fuyutsui:UI_ERROR_MESSAGE(_, errorType, message)
    if message == "目标不在视野中" then
        self:UpdateUnitInSight(state.castTargetUnit)
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
    self:UpdateTargetFullInfo()
    self:UpdateUnitAuraContainer("target")
end

function Fuyutsui:PLAYER_FOCUS_CHANGED()
    self:UpdateFocusFullInfo()
    self:UpdateUnitAuraContainer("focus")
end

function Fuyutsui:UPDATE_MOUSEOVER_UNIT()
    self:UpdateMouseoverFullInfo()
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
    self:AddNameplate(unit)
    self:UpdateTargetCanAttack()
    for index = 1, 5 do
        local boss = "boss" .. index
        self:UpdateUnitCanAttack(boss)
        self:UpdateUnitRangeBlock(boss)
    end
end

function Fuyutsui:NAME_PLATE_UNIT_REMOVED(_, unit)
    nameplate[unit] = nil
    self:UpdateTargetCanAttack()
end

function Fuyutsui:UNIT_THREAT_SITUATION_UPDATE(_, unitTarget)
    if nameplate[unitTarget] then
        self:UpdateNameplateThreat(unitTarget)
        self:UpdateThreatEnemyCounts()
        return
    end
    if unitTarget ~= "player" then return end
    for unit in pairs(nameplate) do
        self:UpdateNameplateThreat(unit)
    end
    self:UpdateThreatEnemyCounts()
end

function Fuyutsui:RefreshShapeshiftAndMount()
    self:UpdateShapeshiftForm()
    self:UpdatePlayerMounted()
end

function Fuyutsui:UPDATE_SHAPESHIFT_FORM()
    self:RefreshShapeshiftAndMount()
end

function Fuyutsui:UPDATE_SHAPESHIFT_FORMS()
    self:RefreshShapeshiftAndMount()
end

function Fuyutsui:ENCOUNTER_START(_, encounterID, encounterName, difficultyID, groupSize)
    self:UpdateEncounterID(encounterID, difficultyID)
end

function Fuyutsui:ENCOUNTER_END(_, encounterID, encounterName, difficultyID, groupSize, success)
    self:UpdateEncounterID(0, 0)
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

function Fuyutsui:OnUpdate(elapsed)
    self:UpdatePlayerCastBlocks()
    self:UpdateUnitCastingOrChannelingInfo("target")
    self:UpdateUnitCastingOrChannelingInfo("focus")
    self:UpdateUnitCastingOrChannelingInfo("mouseover")
    for index = 1, 5 do
        self:UpdateUnitCastingOrChannelingInfo("boss" .. index)
    end
    self:UpdateGroupInRangeAndHealth()

    self.timeElapsed = self.timeElapsed + elapsed
    if self.timeElapsed > 0.2 then
        self:UpdateSpellCooldown()
        self:UpdatePlayerAssistant()
        self:UpdateRune()
        self:UpdateTargetRangeBlock()
        self:UpdateFocusRangeBlock()
        self:UpdateMouseoverRangeBlock()

        self:UpdateEnemyCount()
        self:UpdateItemCooldown()
        self.timeElapsed = 0
    end

    self.timeElapsed1 = self.timeElapsed1 + elapsed
    if self.timeElapsed1 >= 1 then
        self:UpdatePlayerCombatTime()
        self:UpdateKnightStatusCount()
        self.timeElapsed1 = 0
    end
end
