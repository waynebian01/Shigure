local addon, ns = ...

local rc = LibStub("LibRangeCheck-3.0")

local state = Fuyutsui.state
local target = Fuyutsui.target
local focus = Fuyutsui.focus
local mouseover = Fuyutsui.mouseover
local pet = Fuyutsui.pet
local boss = Fuyutsui.boss
local nameplate = Fuyutsui.nameplate

function Fuyutsui:GetUnitRange(unit)
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

local function GetUnitCache(unit)
    if unit == "target" then return target end
    if unit == "focus" then return focus end
    if unit == "mouseover" then return mouseover end
    if unit == "pet" then return pet end
    if boss and boss[unit] then return boss[unit] end
end

local function isSameUnit(unit1, unit2)
    local isSame = UnitIsUnit(unit1, unit2)
    if issecretvalue(isSame) then
        return false
    end
    return isSame
end

local function getUnitTypeIndex(unit)
    if IsInRaid() then
        for index = 1, 40 do
            if isSameUnit(unit, "raid" .. index) then
                return index
            end
        end
    elseif UnitInParty("player") then
        for index = 1, 4 do
            if isSameUnit(unit, "party" .. index) then
                return 41 + index
            end
        end
    end

    if isSameUnit(unit, "player") then
        return 41
    end

    for index = 1, 5 do
        if isSameUnit(unit, "boss" .. index) then
            return 45 + index
        end
    end

    return 51
end

local function getUnitType(unit)
    if not UnitExists(unit) then return 0 end

    local canAttack = UnitCanAttack("player", unit)
    local canAssist = UnitCanAssist("player", unit)
    if not canAttack and not canAssist then return 0 end

    local index = getUnitTypeIndex(unit)
    if canAssist then
        index = index + 100
    end
    return index / 255
end

function Fuyutsui:UpdateUnitType(unit)
    local cache = GetUnitCache(unit)
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
        unitType = getUnitType(unit)
    end
    cache.type = unitType
    self:UpdateStateBlock(category, "类型")
end

function Fuyutsui:UpdateUnitCanAttack(unit)
    local cache = GetUnitCache(unit)
    if not cache then return end
    cache.canAttack = UnitCanAttack("player", unit)
    if boss and boss[unit] then
        cache.canAssist = false
    else
        cache.canAssist = UnitCanAssist("player", unit)
    end
    self:UpdateUnitType(unit)
end

function Fuyutsui:UpdateUnitRangeBlock(unit)
    local cache = GetUnitCache(unit)
    local category = unitZHMap[unit]
    if not cache or not category then return end
    local minRange, maxRange = self:GetUnitRange(unit)
    cache.minRange = minRange
    cache.maxRange = maxRange
    if cache.canAttack then
        if cache.maxRange and self.state.specRange then
            cache.inRange = cache.maxRange <= self.state.specRange
            self:UpdateUnitType(unit)
        end
    elseif cache.canAssist then
        if cache.maxRange then
            cache.inRange = cache.maxRange <= 40
            self:UpdateUnitType(unit)
        end
    end
    self:UpdateStateBlock(category, "距离")
end

function Fuyutsui:UpdateUnitCastingOrChannelingInfo(unit)
    local obj = unitZHMap[unit]
    if obj then
        self:UpdateStateBlock(obj, "施法(倒计时)")
        self:UpdateStateBlock(obj, "施法(正计时)")
        self:UpdateStateBlock(obj, "施法可打断")
        self:UpdateStateBlock(obj, "引导")
        self:UpdateStateBlock(obj, "引导可打断")
    end
end

function Fuyutsui:UpdateUnitDeathStatus(unit)
    local cache = GetUnitCache(unit)
    if not cache then return end
    cache.isDead = UnitIsDeadOrGhost(unit)
    self:UpdateUnitType(unit)
end

function Fuyutsui:UpdateUnitHealthBlock(unit)
    local cache = GetUnitCache(unit)
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
    cache.healthPercent = b or 0
    self:UpdateStateBlock(category, "生命值")
end

function Fuyutsui:UpdateUnitFullInfo(unit)
    self:UpdateUnitCanAttack(unit)
    self:UpdateUnitDeathStatus(unit)
    self:UpdateUnitHealthBlock(unit)
end

-- 目标兼容包装
function Fuyutsui:UpdateTargetType()
    self:UpdateUnitType("target")
end

function Fuyutsui:UpdateTargetCanAttack()
    self:UpdateUnitCanAttack("target")
end

function Fuyutsui:UpdateTargetRangeBlock()
    self:UpdateUnitRangeBlock("target")
end

function Fuyutsui:UpdateTargetDeath()
    self:UpdateUnitDeathStatus("target")
end

function Fuyutsui:UpdateTargetHealth()
    self:UpdateUnitHealthBlock("target")
end

function Fuyutsui:UpdateTargetFullInfo()
    self:UpdateUnitFullInfo("target")
end

-- 焦点包装
function Fuyutsui:UpdateFocusType()
    self:UpdateUnitType("focus")
end

function Fuyutsui:UpdateFocusCanAttack()
    self:UpdateUnitCanAttack("focus")
end

function Fuyutsui:UpdateFocusRangeBlock()
    self:UpdateUnitRangeBlock("focus")
end

function Fuyutsui:UpdateFocusDeath()
    self:UpdateUnitDeathStatus("focus")
end

function Fuyutsui:UpdateFocusHealth()
    self:UpdateUnitHealthBlock("focus")
end

function Fuyutsui:UpdateFocusFullInfo()
    self:UpdateUnitFullInfo("focus")
end

-- 鼠标指向包装
function Fuyutsui:UpdateMouseoverType()
    self:UpdateUnitType("mouseover")
end

function Fuyutsui:UpdateMouseoverCanAttack()
    self:UpdateUnitCanAttack("mouseover")
end

function Fuyutsui:UpdateMouseoverRangeBlock()
    self:UpdateUnitRangeBlock("mouseover")
end

function Fuyutsui:UpdateMouseoverDeath()
    self:UpdateUnitDeathStatus("mouseover")
end

function Fuyutsui:UpdateMouseoverHealth()
    self:UpdateUnitHealthBlock("mouseover")
end

function Fuyutsui:UpdateMouseoverFullInfo()
    self:UpdateUnitFullInfo("mouseover")
end

function Fuyutsui:AddNameplate(unit)
    local minRange, maxRange = self:GetUnitRange(unit)
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

function Fuyutsui:UpdateNameplateThreat(unit)
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

function Fuyutsui:UpdateThreatEnemyCounts()
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

function Fuyutsui:UpdateEnemyCount()
    local count = 0
    local inTestMap = state.mapID and testMap[state.mapID]
    local inTestEncounter = state.encounterID and testEncounter[state.encounterID]
    for unit, data in pairs(nameplate) do
        local minRange, maxRange = self:GetUnitRange(unit)
        data.minRange = minRange
        data.maxRange = maxRange
        data.affectingCombat = UnitAffectingCombat(unit)
        if IsCountedEnemy(self, data, inTestMap, inTestEncounter) then
            count = count + 1
        end
    end
    state.enemyCount = count / 255 or 0
    self:UpdateStateBlock("状态", "敌人数量")
    self:UpdateThreatEnemyCounts()
end
