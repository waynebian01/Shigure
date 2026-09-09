local addon, ns = ...

local rc = LibStub("LibRangeCheck-3.0")

local state = Fuyutsui.state
local target = Fuyutsui.target
local focus = Fuyutsui.focus
local mouseover = Fuyutsui.mouseover
local pet = Fuyutsui.pet
local boss = Fuyutsui.boss
local nameplate = Fuyutsui.nameplate

function Fuyutsui:GetUnitRangeBounds(unit)
    local minRange, maxRange = rc:GetRange(unit)
    return minRange, maxRange
end

local unitZHMap = {
    ["target"] = "目标",
    ["focus"] = "焦点",
    ["mouseover"] = "鼠标",
    ["pet"] = "宠物",
    ["boss1"] = "首领1",
    ["boss2"] = "首领2",
    ["boss3"] = "首领3",
    ["boss4"] = "首领4",
    ["boss5"] = "首领5",
}

local function GetUnitState(unit)
    if unit == "player" then return state end
    if unit == "target" then return target end
    if unit == "focus" then return focus end
    if unit == "mouseover" then return mouseover end
    if unit == "pet" then return pet end
    if boss and boss[unit] then return boss[unit] end
end

local castStateFields = {
    casting = true,
    channeling = true,
    empowering = true,
}

function Fuyutsui:SetTrackedUnitCastState(unit, stateField, isActive)
    local cache = GetUnitState(unit)
    if not cache or not castStateFields[stateField] then return false end
    cache[stateField] = isActive == true
    return true
end

function Fuyutsui:ResetTrackedUnitCastState(unit)
    local cache = GetUnitState(unit)
    if not cache then return end
    cache.casting = false
    cache.channeling = false
    cache.empowering = false
    self:ClearUnitCastStateBlocks(unit, "casting")
    self:ClearUnitCastStateBlocks(unit, "channeling")
end

local function AreSameUnits(unit1, unit2)
    local isSame = UnitIsUnit(unit1, unit2)
    if issecretvalue(isSame) then
        return false
    end
    return isSame
end

local function GetUnitIdentityIndex(unit)
    if IsInRaid() then
        for index = 1, 40 do
            if AreSameUnits(unit, "raid" .. index) then
                return index
            end
        end
    elseif UnitInParty("player") then
        for index = 1, 4 do
            if AreSameUnits(unit, "party" .. index) then
                return 41 + index
            end
        end
    end

    if AreSameUnits(unit, "player") then
        return 41
    end

    for index = 1, 5 do
        if AreSameUnits(unit, "boss" .. index) then
            return 45 + index
        end
    end

    return 51
end

local function EncodeUnitType(unit)
    if not UnitExists(unit) then return 0 end

    local canAttack = UnitCanAttack("player", unit)
    local canAssist = UnitCanAssist("player", unit)
    if not canAttack and not canAssist then return 0 end

    local index = GetUnitIdentityIndex(unit)
    if canAssist then
        index = index + 100
    end
    return index / 255
end

function Fuyutsui:RefreshUnitTypeState(unit)
    local cache = GetUnitState(unit)
    local category = unitZHMap[unit]
    if not cache or not category then return end

    if unit == "pet" then
        cache.exists = UnitExists(unit) and 1 / 255 or 0
        self:UpdateStateBlock(category, "存在")
        return
    end

    if boss and boss[unit] then
        cache.type = (cache.canAttack and 1 or 2) / 255
        self:UpdateStateBlock(category, "类型")
        return
    end

    local unitType = 0
    if not cache.isDead then
        unitType = EncodeUnitType(unit)
    end
    cache.type = unitType
    self:UpdateStateBlock(category, "类型")
end

function Fuyutsui:RefreshUnitReactionState(unit)
    local cache = GetUnitState(unit)
    if not cache then return end
    cache.canAttack = UnitCanAttack("player", unit)
    if boss and boss[unit] then
        cache.canAssist = false
    else
        cache.canAssist = UnitCanAssist("player", unit)
    end
    self:RefreshUnitTypeState(unit)
end

function Fuyutsui:RefreshUnitRangeState(unit)
    local cache = GetUnitState(unit)
    local category = unitZHMap[unit]
    if not cache or not category then return end
    local minRange, maxRange = self:GetUnitRangeBounds(unit)
    cache.minRange = minRange
    cache.maxRange = maxRange
    if cache.canAttack then
        if cache.maxRange and self.state.specRange then
            cache.inRange = cache.maxRange <= self.state.specRange
            self:RefreshUnitTypeState(unit)
        end
    elseif cache.canAssist then
        if cache.maxRange then
            cache.inRange = cache.maxRange <= 40
            self:RefreshUnitTypeState(unit)
        end
    end
    self:UpdateStateBlock(category, "距离")
end

function Fuyutsui:RefreshUnitCastStateBlocks(unit)
    local cache = GetUnitState(unit)
    local category = unitZHMap[unit]
    if not cache or not category or not UnitExists(unit) then return end

    if cache.casting then
        self:UpdateStateBlock(category, "施法(倒计时)")
        self:UpdateStateBlock(category, "施法(正计时)")
        self:UpdateStateBlock(category, "施法可打断")
    end
    if cache.channeling or cache.empowering then
        self:UpdateStateBlock(category, "引导")
        self:UpdateStateBlock(category, "引导可打断")
    end
end

function Fuyutsui:ClearUnitCastStateBlocks(unit, stateField)
    local cache = GetUnitState(unit)
    local category = unitZHMap[unit]
    if not cache or not category then return end

    if stateField == "casting" then
        self:UpdateStateBlock(category, "施法(倒计时)")
        self:UpdateStateBlock(category, "施法(正计时)")
        self:UpdateStateBlock(category, "施法可打断")
    elseif (stateField == "channeling" or stateField == "empowering")
        and not cache.channeling and not cache.empowering then
        self:UpdateStateBlock(category, "引导")
        self:UpdateStateBlock(category, "引导可打断")
    end
end

function Fuyutsui:RefreshUnitDeathState(unit)
    local cache = GetUnitState(unit)
    if not cache then return end
    cache.isDead = UnitIsDeadOrGhost(unit)
    self:RefreshUnitTypeState(unit)
end

function Fuyutsui:RefreshUnitHealthState(unit)
    local cache = GetUnitState(unit)
    local category = unitZHMap[unit]
    if not cache or not category then return end
    if not UnitExists(unit) then
        cache.healthPercent = 0
        self:UpdateStateBlock(category, "生命值")
        return
    end
    local healthPercent = UnitHealthPercent(unit, false, self.curve100)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    cache.healthPercent = b
    self:UpdateStateBlock(category, "生命值")
end

function Fuyutsui:RefreshUnitState(unit)
    self:ResetTrackedUnitCastState(unit)
    self:RefreshUnitReactionState(unit)
    self:RefreshUnitDeathState(unit)
    self:RefreshUnitHealthState(unit)
end

-- 目标兼容包装
function Fuyutsui:RefreshTargetTypeState()
    self:RefreshUnitTypeState("target")
end

function Fuyutsui:RefreshTargetReactionState()
    self:RefreshUnitReactionState("target")
end

function Fuyutsui:RefreshTargetRangeState()
    self:RefreshUnitRangeState("target")
end

function Fuyutsui:RefreshTargetDeathState()
    self:RefreshUnitDeathState("target")
end

function Fuyutsui:RefreshTargetHealthState()
    self:RefreshUnitHealthState("target")
end

function Fuyutsui:RefreshTargetState()
    self:RefreshUnitState("target")
end

-- 焦点包装
function Fuyutsui:RefreshFocusTypeState()
    self:RefreshUnitTypeState("focus")
end

function Fuyutsui:RefreshFocusReactionState()
    self:RefreshUnitReactionState("focus")
end

function Fuyutsui:RefreshFocusRangeState()
    self:RefreshUnitRangeState("focus")
end

function Fuyutsui:RefreshFocusDeathState()
    self:RefreshUnitDeathState("focus")
end

function Fuyutsui:RefreshFocusHealthState()
    self:RefreshUnitHealthState("focus")
end

function Fuyutsui:RefreshFocusState()
    self:RefreshUnitState("focus")
end

-- 鼠标指向包装
function Fuyutsui:RefreshMouseoverTypeState()
    self:RefreshUnitTypeState("mouseover")
end

function Fuyutsui:RefreshMouseoverReactionState()
    self:RefreshUnitReactionState("mouseover")
end

function Fuyutsui:RefreshMouseoverRangeState()
    self:RefreshUnitRangeState("mouseover")
end

function Fuyutsui:RefreshMouseoverDeathState()
    self:RefreshUnitDeathState("mouseover")
end

function Fuyutsui:RefreshMouseoverHealthState()
    self:RefreshUnitHealthState("mouseover")
end

function Fuyutsui:RefreshMouseoverState()
    self:RefreshUnitState("mouseover")
end

function Fuyutsui:CacheNameplateUnit(unit)
    local minRange, maxRange = self:GetUnitRangeBounds(unit)
    nameplate[unit] = {
        name = GetUnitName(unit, true),
        GUID = UnitGUID(unit),
        canAttack = UnitCanAttack("player", unit),
        canAssist = UnitCanAssist("player", unit),
        minRange = minRange,
        maxRange = maxRange,
        affectingCombat = UnitAffectingCombat(unit),
        threatStatus = UnitThreatSituation("player", unit),
    }
end

function Fuyutsui:RefreshNameplateThreat(unit)
    local data = nameplate[unit]
    if not data then return end
    data.threatStatus = UnitThreatSituation("player", unit)
end

local testMap = {
    [2393] = true,
}
local testEncounter = {
    [2563] = true,
}

local function IsCountedEnemy(self, data, inTestMap, inTestEncounter)
    return data.canAttack and data.maxRange and data.maxRange <= self.state.specRange
        and (data.affectingCombat or inTestMap or inTestEncounter)
end

function Fuyutsui:RefreshThreatEnemyCounts()
    local noThreatCount = 0
    local threatCount = 0
    local inTestMap = state.mapID and testMap[state.mapID]
    local inTestEncounter = state.encounterID and testEncounter[state.encounterID]
    for _, data in pairs(nameplate) do
        if IsCountedEnemy(self, data, inTestMap, inTestEncounter) then
            if data.threatStatus and data.threatStatus >= 2 then
                threatCount = threatCount + 1
            else
                noThreatCount = noThreatCount + 1
            end
        end
    end
    state.noThreatEnemyCount = noThreatCount / 255 or 0
    state.threatEnemyCount = threatCount / 255 or 0
    self:UpdateStateBlock("状态", "敌人数-无仇恨")
    self:UpdateStateBlock("状态", "敌人数-有仇恨")
end

function Fuyutsui:RefreshEnemyCounts()
    local count = 0
    local inTestMap = state.mapID and testMap[state.mapID]
    local inTestEncounter = state.encounterID and testEncounter[state.encounterID]
    for unit, data in pairs(nameplate) do
        local minRange, maxRange = self:GetUnitRangeBounds(unit)
        data.minRange = minRange
        data.maxRange = maxRange
        data.affectingCombat = UnitAffectingCombat(unit)
        if IsCountedEnemy(self, data, inTestMap, inTestEncounter) then
            count = count + 1
        end
    end
    state.enemyCount = count / 255 or 0
    self:UpdateStateBlock("状态", "敌人数量")
    self:RefreshThreatEnemyCounts()
end
