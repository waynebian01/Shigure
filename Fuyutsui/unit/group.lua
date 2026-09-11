local addon, ns = ...

local EvaluateColorFromBoolean = C_CurveUtil.EvaluateColorFromBoolean

local state = Fuyutsui.state
local roleMap = Fuyutsui.roleMap
local groupHealthCurves = Fuyutsui.groupHealthCurves
local ColorValue0 = CreateColor(0, 0, 0, 1)
local updateIndex = 1
local GROUP_MAX_MEMBERS = 30

-- 治疗法术直接选择登录时预创建的生命曲线，禁止在战斗中创建曲线。
local helpfulSpellCurves = {
    [2061] = groupHealthCurves.incoming15,
    [1262763] = groupHealthCurves.incoming15,
    [82326] = groupHealthCurves.incoming40,
    [19750] = groupHealthCurves.incoming15,
    [8936] = groupHealthCurves.incoming15,
    [186263] = groupHealthCurves.incoming40,
    [77472] = groupHealthCurves.incoming15,
}

function Fuyutsui:IterateGroupMembers(reversed, forceParty)
    local unit = (not forceParty and IsInRaid()) and 'raid' or 'party'
    local numGroupMembers = unit == 'party' and GetNumSubgroupMembers() or GetNumGroupMembers()
    local i = reversed and numGroupMembers or (unit == 'party' and 0 or 1)
    return function()
        local ret
        if i == 0 and unit == 'party' then
            ret = 'player'
        elseif i <= numGroupMembers and i > 0 then
            ret = unit .. i
        end
        i = i + (reversed and -1 or 1)
        return ret
    end
end

function Fuyutsui:RefreshGroupMemberHealth(unit)
    local blocks = self.blocks
    local group = self.group
    local obj = group[unit]
    if not blocks or not blocks.groups or not blocks.groups.healthPercent or not obj then return end
    local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.healthPercent
    local healthPercent = UnitHealthPercent(unit, true, obj.curve or groupHealthCurves.default)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    obj.healthPercent = b
    self:CreateTexture(index, obj.healthPercent)
end

function Fuyutsui:RefreshGroupMemberValidity(unit)
    local obj = self.group[unit]
    if not obj then return end
    obj.valid = not obj.isDead and obj.canAssist and obj.inSight
end

function Fuyutsui:RefreshNextGroupMemberState()
    local blocks = self.blocks
    local group = self.group
    local groupList = self.groupList
    if not blocks or not blocks.groups or not blocks.groups.role then return end
    local numUnits = #groupList
    if numUnits < 1 then
        updateIndex = 1
        return
    end

    if updateIndex < 1 or updateIndex > numUnits then
        updateIndex = 1
    end

    local unit = groupList[updateIndex]
    local obj = unit and group[unit] or nil
    if not obj then
        updateIndex = 1
        return
    end

    local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.role
    obj.isDead = UnitIsDeadOrGhost(unit)
    obj.canAssist = UnitCanAssist("player", unit)
    obj.valid = not obj.isDead and obj.canAssist and obj.inSight
    if obj.valid then
        local inRange = UnitIsUnit(unit, "player") and true or UnitInRange(unit)
        local roleValue = roleMap[obj.role] and roleMap[obj.role] / 255 or 5 / 255
        local trueValue = CreateColor(0, 0, roleValue, 1)
        local booleanValue = EvaluateColorFromBoolean(inRange, trueValue, ColorValue0)
        local _, _, b = booleanValue:GetRGB()
        self:CreateTexture(index, b)
    else
        self:CreateTexture(index, 0)
    end

    updateIndex = updateIndex % numUnits + 1
end

--- source: "guid" | "health" | nil
function Fuyutsui:RefreshGroupMemberDeath(unitOrGuid, source)
    local group = self.group
    if source == "guid" then
        for unit, data in pairs(group) do
            if data.GUID == unitOrGuid then
                data.isDead = true
                self:RefreshGroupMemberValidity(unit)
            end
        end
        return
    end

    local obj = group[unitOrGuid]
    if not obj then return end
    obj.isDead = UnitIsDeadOrGhost(unitOrGuid)
    self:RefreshGroupMemberValidity(unitOrGuid)
end

function Fuyutsui:MarkGroupMemberTemporarilyOutOfSight(unit)
    local obj = self.group[unit]
    if not obj then return end
    obj.inSight = false
    if obj.inSightTimer then
        obj.inSightTimer:Cancel()
        obj.inSightTimer = nil
    end
    obj.inSightTimer = C_Timer.NewTimer(1.5, function()
        obj.inSight = true
        obj.inSightTimer = nil
        Fuyutsui:RefreshGroupMemberValidity(unit)
    end)
    self:RefreshGroupMemberValidity(unit)
end

function Fuyutsui:RecordIncomingHealEstimate(spellID)
    local unit = state.castTargetUnit
    if not unit then return end
    local obj = self.group[unit]
    if not obj then return end
    local curve = helpfulSpellCurves[spellID]
    if curve then
        obj.curve = curve
        self:RefreshGroupMemberHealth(unit)
    end
end

function Fuyutsui:ClearIncomingHealEstimates()
    for unit, data in pairs(self.group) do
        if data.curve ~= groupHealthCurves.default then
            data.curve = groupHealthCurves.default
            self:RefreshGroupMemberHealth(unit)
        end
    end
end

function Fuyutsui:ClearGroupStateBlocks()
    local blocks = self.blocks
    local groups = blocks and blocks.groups or nil
    if not groups or not groups.start or not groups.num then
        return
    end

    local endIndex = groups.start + GROUP_MAX_MEMBERS * groups.num
    for index = groups.start, endIndex do
        self:CreateTexture(index, 0)
    end
end

function Fuyutsui:RebuildGroupRoster()
    self:ClearGroupStateBlocks()
    updateIndex = 1
    self.group = {}
    self.groupList = {}
    local group = self.group
    local groupList = self.groupList
    local i = 1
    for unit in self:IterateGroupMembers() do
        table.insert(groupList, unit)
        local role = UnitGroupRolesAssigned(unit)
        if unit == "player" then
            role = self.state.specRole
        end
        group[unit] = {
            index = i,
            name = GetUnitName(unit, true),
            GUID = UnitGUID(unit),
            role = role,
            isDead = UnitIsDeadOrGhost(unit),
            inRange = UnitInRange(unit),
            canAttack = UnitCanAttack("player", unit),
            canAssist = UnitCanAssist("player", unit),
            inSight = true,
            inSightTimer = nil,
            curve = groupHealthCurves.default,
        }
        self:RefreshGroupMemberValidity(unit)
        self:RefreshGroupMemberHealth(unit)
        i = i + 1
    end
    if self.RefreshGroupAuraContainers then
        self:RefreshGroupAuraContainers()
    end
    if self.RefreshGroupHealAbsorbBars then
        self:RefreshGroupHealAbsorbBars()
    end
end
