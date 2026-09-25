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

local activeTimers = {}
local eventTypeIndex = nil
local eventKeyIndex = nil
local eventTypeIndexSource = nil

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

local function AddOrUpdateTimer(payload)
    if type(payload) ~= "table" or not PayloadMatchesEncounter(payload) then
        return
    end

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
    activeTimers[ResolveTimerKey(payload)] = {
        timerID = SafeNumber(payload.timerID),
        eventID = eventID,
        typeCode = typeCode,
        eventKey = eventKey,
        castTime = castTime,
    }
end

local function RemoveTimer(payload)
    if type(payload) ~= "table" then return end

    local timerID = SafeNumber(payload.timerID)
    if timerID then
        activeTimers["timer:" .. tostring(timerID)] = nil
        return
    end

    local eventID = SafeNumber(payload.eventID)
    if not eventID then return end
    for key, timer in pairs(activeTimers) do
        if timer.eventID == eventID then
            activeTimers[key] = nil
        end
    end
end

local function SetPixelStates(typeCode, eventKey, countdownSeconds)
    typeCode = math.max(0, math.min(6, math.floor(SafeNumber(typeCode) or 0)))
    eventKey = math.max(0, math.min(255, math.floor(SafeNumber(eventKey) or 0)))
    countdownSeconds = math.max(0, math.min(MAX_COUNTDOWN_SECONDS,
        math.floor((SafeNumber(countdownSeconds) or 0) + 0.5)))

    local typePixel = typeCode / 255
    local eventPixel = eventKey / 255
    local countdownPixel = countdownSeconds / 255
    if state.exBossSkillType ~= typePixel then
        state.exBossSkillType = typePixel
        Fuyutsui:UpdateStateBlock("特殊", "EX首领技能类型")
    end
    if state.exBossSkillEvent ~= eventPixel then
        state.exBossSkillEvent = eventPixel
        Fuyutsui:UpdateStateBlock("特殊", "EX首领技能事件")
    end
    if state.exBossSkillCountdown ~= countdownPixel then
        state.exBossSkillCountdown = countdownPixel
        Fuyutsui:UpdateStateBlock("特殊", "EX首领技能倒计时")
    end
end

function Fuyutsui:RefreshExBossTimelinePixels()
    if not self.exBossTimelineBridgeInitialized then
        self:InitializeExBossTimelineBridge()
    end

    local now = GetNow()
    local selected = nil
    local selectedRemaining = nil
    for key, timer in pairs(activeTimers) do
        local castTime = SafeNumber(timer.castTime)
        local remaining = castTime and (castTime - now) or nil
        if not remaining or remaining < -TIMER_EXPIRE_GRACE_SECONDS then
            activeTimers[key] = nil
        elseif not selectedRemaining or remaining < selectedRemaining then
            selected = timer
            selectedRemaining = remaining
        end
    end

    if not selected then
        SetPixelStates(0, 0, 0)
        return
    end

    -- ceil 保证剩余不足 1 秒时仍输出 1；到达预计施放时刻后输出 0。
    local countdown = selectedRemaining > 0 and math.ceil(selectedRemaining) or 0
    SetPixelStates(selected.typeCode, selected.eventKey, countdown)
end

function Fuyutsui:ResetExBossTimelinePixels()
    wipe(activeTimers)
    SetPixelStates(0, 0, 0)
end

function Fuyutsui:InitializeExBossTimelineBridge()
    if self.exBossTimelineBridgeInitialized then return true end

    local events = _G.ExwindTools
    if not events or type(events.RegisterEvent) ~= "function" then
        return false
    end

    events:RegisterEvent("EXBOSS_FIXED_AI_EVENT_SCHEDULED", EVENT_OWNER_PREFIX .. ".Scheduled",
        function(_, payload)
            AddOrUpdateTimer(payload)
            Fuyutsui:RefreshExBossTimelinePixels()
        end)
    events:RegisterEvent("EXBOSS_FIXED_AI_EVENT_FINISHED", EVENT_OWNER_PREFIX .. ".Finished",
        function(_, payload)
            RemoveTimer(payload)
            Fuyutsui:RefreshExBossTimelinePixels()
        end)
    -- TIME 轴及其它能提供稳定 eventID 的计时器至少会在 5 秒节点进入像素桥。
    events:RegisterEvent("EXBOSS_TIMER_FIVE_SEC_REMAINING", EVENT_OWNER_PREFIX .. ".FiveSeconds",
        function(_, payload)
            AddOrUpdateTimer(payload)
            Fuyutsui:RefreshExBossTimelinePixels()
        end)

    self.exBossTimelineBridgeInitialized = true
    return true
end
