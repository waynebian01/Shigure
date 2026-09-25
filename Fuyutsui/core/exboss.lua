local addon, ns = ...

local state = Fuyutsui.state
local EVENT_OWNER_PREFIX = "Fuyutsui.ExBossTimeline"
local TIMER_EXPIRE_GRACE_SECONDS = 1.5
local MAX_COUNTDOWN_SECONDS = 255

-- 像素值：0=无/未知，1=其他，2=坦克，3=治疗，4=点名，5=机制，6=特殊。
local EVENT_TYPE_CODES = {
    ["其他"] = 1,
    ["坦克"] = 2,
    ["治疗"] = 3,
    ["点名"] = 4,
    ["机制"] = 5,
    ["特殊"] = 6,
}

local activeBossTimers = {}
local activeTrashTimers = {}
local eventTypeIndex = nil
local eventKeyIndex = nil
local eventTypeIndexSource = nil
local trashTypeIndex = nil
local trashKeyIndex = nil
local trashIndexSource = nil

local function SafeNumber(value)
    local ok, number = pcall(tonumber, value)
    return ok and number or nil
end

local function GetNow()
    return GetTime and GetTime() or 0
end

local function BuildEventIndexes()
    local data = _G.EXBOSS_ENCOUNTER_DATA
    if eventTypeIndex and eventKeyIndex and eventTypeIndexSource == data then
        return eventTypeIndex, eventKeyIndex
    end

    local typeIndex = {}
    local eventIDs = {}
    local seenEventIDs = {}
    local maps = type(data) == "table" and data.maps or nil
    if type(maps) == "table" then
        for _, map in pairs(maps) do
            local bosses = type(map) == "table" and map.bosses or nil
            if type(bosses) == "table" then
                for _, boss in pairs(bosses) do
                    local events = type(boss) == "table" and boss.events or nil
                    if type(events) == "table" then
                        for rawEventID, event in pairs(events) do
                            local eventID = SafeNumber(rawEventID)
                                or SafeNumber(type(event) == "table" and event.eventID or nil)
                            local eventType = type(event) == "table" and event.eventType or nil
                            if eventID and type(eventType) == "string" then
                                typeIndex[eventID] = EVENT_TYPE_CODES[eventType] or 0
                                if not seenEventIDs[eventID] then
                                    seenEventIDs[eventID] = true
                                    eventIDs[#eventIDs + 1] = eventID
                                end
                            end
                        end
                    end
                end
            end
        end
    end

    table.sort(eventIDs)
    local keyIndex = {}
    for key, eventID in ipairs(eventIDs) do
        -- 一个像素只能承载 0..255；超出的事件不输出键，但仍可输出类型和倒计时。
        if key <= 255 then
            keyIndex[eventID] = key
        end
    end

    eventTypeIndex = typeIndex
    eventKeyIndex = keyIndex
    eventTypeIndexSource = data
    Fuyutsui.ExBossEventIDs = eventIDs
    Fuyutsui.ExBossEventKeyByID = keyIndex
    return typeIndex, keyIndex
end

local function BuildTrashIndexes()
    local data = _G.EXBOSS_TRASH_CD_DATA
    if trashTypeIndex and trashKeyIndex and trashIndexSource == data then
        return trashTypeIndex, trashKeyIndex
    end

    local typeIndex = {}
    local spellIDs = {}
    local seenSpellIDs = {}
    if type(data) == "table" then
        for _, map in pairs(data) do
            local mobs = type(map) == "table" and map.mobs or nil
            if type(mobs) == "table" then
                for _, mob in pairs(mobs) do
                    local spells = type(mob) == "table" and mob.spells or nil
                    if type(spells) == "table" then
                        for rawSpellID, spell in pairs(spells) do
                            local spellID = SafeNumber(rawSpellID)
                                or SafeNumber(type(spell) == "table" and spell.spellID or nil)
                            local eventType = type(spell) == "table" and spell.eventType or nil
                            if spellID then
                                if type(eventType) == "string" then
                                    typeIndex[spellID] = EVENT_TYPE_CODES[eventType] or 0
                                end
                                if not seenSpellIDs[spellID] then
                                    seenSpellIDs[spellID] = true
                                    spellIDs[#spellIDs + 1] = spellID
                                end
                            end
                        end
                    end
                end
            end
        end
    end

    table.sort(spellIDs)
    local keyIndex = {}
    for key, spellID in ipairs(spellIDs) do
        if key <= 255 then
            keyIndex[spellID] = key
        end
    end

    trashTypeIndex = typeIndex
    trashKeyIndex = keyIndex
    trashIndexSource = data
    Fuyutsui.ExTrashSpellIDs = spellIDs
    Fuyutsui.ExTrashEventKeyBySpellID = keyIndex
    return typeIndex, keyIndex
end

local function ResolveTimerKey(payload)
    local timerID = SafeNumber(type(payload) == "table" and payload.timerID or nil)
    if timerID then
        return "timer:" .. tostring(timerID)
    end

    local eventID = SafeNumber(type(payload) == "table" and payload.eventID or nil) or 0
    local castTime = SafeNumber(type(payload) == "table" and payload.castTime or nil) or 0
    return "event:" .. tostring(eventID) .. ":" .. tostring(math.floor(castTime * 10 + 0.5))
end

local function PayloadMatchesEncounter(payload)
    local payloadEncounterID = SafeNumber(type(payload) == "table" and payload.encounterID or nil)
    local currentEncounterID = SafeNumber(state.encounterID)
    return not (payloadEncounterID and currentEncounterID and currentEncounterID > 0
        and payloadEncounterID ~= currentEncounterID)
end

local function AddOrUpdateBossTimer(payload)
    if type(payload) ~= "table" or not PayloadMatchesEncounter(payload) then
        return
    end
    if payload.source == "trash" then return end

    local eventID = SafeNumber(payload.eventID)
    local castTime = SafeNumber(payload.castTime)
    if not castTime then
        local remaining = SafeNumber(payload.remaining)
        if remaining then
            castTime = GetNow() + math.max(0, remaining)
        end
    end
    if not castTime then
        return
    end

    local typeIndex, keyIndex = BuildEventIndexes()
    local typeCode = eventID and (typeIndex[eventID] or 0) or 0
    local eventKey = eventID and (keyIndex[eventID] or 0) or 0
    activeBossTimers[ResolveTimerKey(payload)] = {
        timerID = SafeNumber(payload.timerID),
        eventID = eventID,
        typeCode = typeCode,
        eventKey = eventKey,
        castTime = castTime,
    }
end

local function AddOrUpdateTrashTimer(payload)
    if type(payload) ~= "table" or payload.source ~= "trash" then
        return
    end

    local spellID = SafeNumber(payload.spellID)
    local castTime = SafeNumber(payload.castTime)
    if not castTime then
        local remaining = SafeNumber(payload.remaining)
        if remaining then
            castTime = GetNow() + math.max(0, remaining)
        end
    end
    if not spellID or not castTime then
        return
    end

    local typeIndex, keyIndex = BuildTrashIndexes()
    activeTrashTimers[ResolveTimerKey(payload)] = {
        timerID = SafeNumber(payload.timerID),
        spellID = spellID,
        typeCode = typeIndex[spellID] or 0,
        eventKey = keyIndex[spellID] or 0,
        castTime = castTime,
    }
end

local function RemoveBossTimer(payload)
    if type(payload) ~= "table" then return end

    local timerID = SafeNumber(payload.timerID)
    if timerID then
        activeBossTimers["timer:" .. tostring(timerID)] = nil
        return
    end

    local eventID = SafeNumber(payload.eventID)
    if not eventID then return end
    for key, timer in pairs(activeBossTimers) do
        if timer.eventID == eventID then
            activeBossTimers[key] = nil
        end
    end
end

local function RemoveTrashTimer(payload)
    if type(payload) ~= "table" then return end

    local timerID = SafeNumber(payload.timerID)
    local spellID = SafeNumber(payload.spellID)
    local runtime = type(payload.runtime) == "table" and payload.runtime or nil
    local timerIDs = runtime and type(runtime.localTimerIDsBySpellID) == "table"
        and runtime.localTimerIDsBySpellID or nil
    timerID = timerID or (spellID and SafeNumber(timerIDs and timerIDs[spellID] or nil))
    if timerID then
        activeTrashTimers["timer:" .. tostring(timerID)] = nil
        return
    end

    if not spellID then return end
    for key, timer in pairs(activeTrashTimers) do
        if timer.spellID == spellID then
            activeTrashTimers[key] = nil
        end
    end
end

local function SetPixelStates(typeCode, eventKey, countdownSeconds, stateKeys, fieldNames)
    typeCode = math.max(0, math.min(6, math.floor(SafeNumber(typeCode) or 0)))
    eventKey = math.max(0, math.min(255, math.floor(SafeNumber(eventKey) or 0)))
    countdownSeconds = math.max(0, math.min(MAX_COUNTDOWN_SECONDS,
        math.floor((SafeNumber(countdownSeconds) or 0) + 0.5)))

    local typePixel = typeCode / 255
    local eventPixel = eventKey / 255
    local countdownPixel = countdownSeconds / 255
    if state[stateKeys[1]] ~= typePixel then
        state[stateKeys[1]] = typePixel
        Fuyutsui:UpdateStateBlock("特殊", fieldNames[1])
    end
    if state[stateKeys[2]] ~= eventPixel then
        state[stateKeys[2]] = eventPixel
        Fuyutsui:UpdateStateBlock("特殊", fieldNames[2])
    end
    if state[stateKeys[3]] ~= countdownPixel then
        state[stateKeys[3]] = countdownPixel
        Fuyutsui:UpdateStateBlock("特殊", fieldNames[3])
    end
end

local BOSS_STATE_KEYS = { "exBossSkillType", "exBossSkillEvent", "exBossSkillCountdown" }
local BOSS_FIELD_NAMES = { "EX首领技能类型", "EX首领技能事件", "EX首领技能倒计时" }
local TRASH_STATE_KEYS = { "exTrashSkillType", "exTrashSkillEvent", "exTrashSkillCountdown" }
local TRASH_FIELD_NAMES = { "EX小怪技能类型", "EX小怪技能事件", "EX小怪技能倒计时" }

local function SelectNearestTimer(timers)
    local now = GetNow()
    local selected = nil
    local selectedRemaining = nil
    for key, timer in pairs(timers) do
        local castTime = SafeNumber(timer.castTime)
        local remaining = castTime and (castTime - now) or nil
        if not remaining or remaining < -TIMER_EXPIRE_GRACE_SECONDS then
            timers[key] = nil
        elseif not selectedRemaining or remaining < selectedRemaining then
            selected = timer
            selectedRemaining = remaining
        end
    end
    return selected, selectedRemaining
end

function Fuyutsui:RefreshExBossTimelinePixels()
    if not self.exBossTimelineBridgeInitialized then
        self:InitializeExBossTimelineBridge()
    end

    local selected, selectedRemaining = SelectNearestTimer(activeBossTimers)

    if not selected then
        SetPixelStates(0, 0, 0, BOSS_STATE_KEYS, BOSS_FIELD_NAMES)
        return
    end

    -- ceil 保证剩余不足 1 秒时仍输出 1；到达预计施放时刻后输出 0。
    local countdown = selectedRemaining > 0 and math.ceil(selectedRemaining) or 0
    SetPixelStates(selected.typeCode, selected.eventKey, countdown, BOSS_STATE_KEYS, BOSS_FIELD_NAMES)
end

function Fuyutsui:RefreshExTrashTimelinePixels()
    if not self.exBossTimelineBridgeInitialized then
        self:InitializeExBossTimelineBridge()
    end

    local selected, selectedRemaining = SelectNearestTimer(activeTrashTimers)
    if not selected then
        SetPixelStates(0, 0, 0, TRASH_STATE_KEYS, TRASH_FIELD_NAMES)
        return
    end

    local countdown = selectedRemaining > 0 and math.ceil(selectedRemaining) or 0
    SetPixelStates(selected.typeCode, selected.eventKey, countdown, TRASH_STATE_KEYS, TRASH_FIELD_NAMES)
end

function Fuyutsui:ResetExBossTimelinePixels()
    wipe(activeBossTimers)
    wipe(activeTrashTimers)
    SetPixelStates(0, 0, 0, BOSS_STATE_KEYS, BOSS_FIELD_NAMES)
    SetPixelStates(0, 0, 0, TRASH_STATE_KEYS, TRASH_FIELD_NAMES)
end

function Fuyutsui:InitializeExBossTimelineBridge()
    if self.exBossTimelineBridgeInitialized then return true end

    local events = _G.ExwindTools
    if not events or type(events.RegisterEvent) ~= "function" then
        return false
    end

    events:RegisterEvent("EXBOSS_FIXED_AI_EVENT_SCHEDULED", EVENT_OWNER_PREFIX .. ".Scheduled",
        function(_, payload)
            AddOrUpdateBossTimer(payload)
            Fuyutsui:RefreshExBossTimelinePixels()
        end)
    events:RegisterEvent("EXBOSS_FIXED_AI_EVENT_FINISHED", EVENT_OWNER_PREFIX .. ".Finished",
        function(_, payload)
            RemoveBossTimer(payload)
            Fuyutsui:RefreshExBossTimelinePixels()
        end)
    -- TIME 轴及其它能提供稳定 eventID 的计时器至少会在 5 秒节点进入像素桥。
    events:RegisterEvent("EXBOSS_TIMER_FIVE_SEC_REMAINING", EVENT_OWNER_PREFIX .. ".FiveSeconds",
        function(_, payload)
            if type(payload) == "table" and payload.source == "trash" then
                AddOrUpdateTrashTimer(payload)
                Fuyutsui:RefreshExTrashTimelinePixels()
            else
                AddOrUpdateBossTimer(payload)
                Fuyutsui:RefreshExBossTimelinePixels()
            end
        end)
    events:RegisterEvent("EXBOSS_TRASH_OBSERVED_CAST_START", EVENT_OWNER_PREFIX .. ".TrashCastStart",
        function(_, payload)
            RemoveTrashTimer(payload)
            Fuyutsui:RefreshExTrashTimelinePixels()
        end)

    self.exBossTimelineBridgeInitialized = true
    return true
end
