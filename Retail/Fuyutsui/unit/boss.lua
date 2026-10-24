local addon, ns = ...

local state = Fuyutsui.state
local BOSS_UNIT_COUNT = 5

function Fuyutsui:IsBossUnit(unit)
    return type(unit) == "string" and unit:match("^boss[1-5]$") ~= nil
end

function Fuyutsui:RefreshBossUnitStates()
    for index = 1, BOSS_UNIT_COUNT do
        local unit = "boss" .. index
        self:RefreshUnitState(unit)
        self:RefreshUnitRangeState(unit)
    end
end

function Fuyutsui:RefreshBossReactionAndRangeStates()
    for index = 1, BOSS_UNIT_COUNT do
        local unit = "boss" .. index
        self:RefreshUnitReactionState(unit)
        self:RefreshUnitRangeState(unit)
    end
end

function Fuyutsui:RefreshBossAuraContainers()
    for index = 1, BOSS_UNIT_COUNT do
        self:UpdateUnitAuraContainer("boss" .. index)
    end
end

function Fuyutsui:RefreshBossCastStateBlocks(refreshSafely)
    for index = 1, BOSS_UNIT_COUNT do
        local unit = "boss" .. index
        if refreshSafely then
            refreshSafely(self, "RefreshUnitCastStateBlocks", unit)
        else
            self:RefreshUnitCastStateBlocks(unit)
        end
    end
end

function Fuyutsui:SetEncounterState(encounterID, difficultyID)
    state.encounterID = encounterID
    local bossIndex = self.bossID and self.bossID[encounterID] or 0
    state.bossID = bossIndex / 255
    self:UpdateStateBlock("区域", "首领战")

    state.encounterDifficultyID = difficultyID
    self:UpdateStateBlock("区域", "首领战难度")
end
