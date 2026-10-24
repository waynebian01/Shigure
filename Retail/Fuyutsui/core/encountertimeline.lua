local addon, ns = ...

-- 记录当前 Encounter Timeline 实例。
-- 首领事件的 spellID、名称、图标、严重程度和颜色是秘密值：可以保存，不能比较、运算或当作表键。
-- 剩余时间只在事件加入或状态变化时向接口读一次，之后用本地锚点推算。

local isSec = issecretvalue

local EventState = Enum and Enum.EncounterTimelineEventState or {}
local STATE_PAUSED = EventState.Paused or 1
local STATE_FINISHED = EventState.Finished or 2
local STATE_CANCELED = EventState.Canceled or 3

local eventColors = {}
Fuyutsui.EncounterTimelineSlotCount = 5
Fuyutsui.EncounterTimelinePixelCount = Fuyutsui.EncounterTimelineSlotCount * 3

local function TimePixel(seconds)
    return math.min(255, math.max(0, math.floor(seconds * 10 + 0.5))) / 255
end

Fuyutsui.encounterTimeline = {
    available = false,
    enabled = false,
    viewActive = false,
    viewType = nil,
    highlightTime = nil,
    hasActiveEvents = false,
    hasAnyEvents = false,
    hasPausedEvents = false,
    hasVisibleEvents = false,
    tracks = {},
    events = {},
}

local function AssignPublic(record, field, value)
    if value == nil or isSec(value) then
        return
    end
    record[field] = value
end

local function CreateRecord(eventInfo)
    local record = {
        id = eventInfo.id,
        source = eventInfo.source,
        duration = eventInfo.duration,
        maxQueueDuration = eventInfo.maxQueueDuration,
        state = nil,
        timeElapsed = nil,
        timeRemaining = nil,
        anchorTime = nil,
        timingPaused = false,
        track = nil,
        trackSortIndex = nil,
        blocked = false,
        highlighted = false,
    }

    AssignPublic(record, "iconFileID", eventInfo.iconFileID)
    AssignPublic(record, "icons", eventInfo.icons)
    AssignPublic(record, "severity", eventInfo.severity)
    AssignPublic(record, "isApproximate", eventInfo.isApproximate)
    record.spellID = eventInfo.spellID
    record.spellName = eventInfo.spellName
    return record
end

local function IsTimingFrozen(state)
    return state == STATE_PAUSED or state == STATE_FINISHED or state == STATE_CANCELED
end

local function AnchorTiming(record)
    record.anchorTime = GetTime()
    record.timingPaused = IsTimingFrozen(record.state)
end

function Fuyutsui:GetEncounterTimelineEventColor(eventID)
    return eventColors[eventID]
end

function Fuyutsui:RefreshEncounterTimelineStatus()
    local timeline = self.encounterTimeline
    if not C_EncounterTimeline then
        timeline.available = false
        timeline.enabled = false
        return
    end

    timeline.available = C_EncounterTimeline.IsFeatureAvailable()
    timeline.enabled = C_EncounterTimeline.IsFeatureEnabled()
    if not timeline.available then
        return
    end

    local viewType = C_EncounterTimeline.GetViewType()
    if viewType ~= nil and not isSec(viewType) then
        timeline.viewType = viewType
    end

    local highlightTime = C_EncounterTimeline.GetEventHighlightTime()
    if highlightTime ~= nil and not isSec(highlightTime) then
        timeline.highlightTime = highlightTime
    end

    timeline.hasActiveEvents = C_EncounterTimeline.HasActiveEvents()
    timeline.hasAnyEvents = C_EncounterTimeline.HasAnyEvents()
    timeline.hasPausedEvents = C_EncounterTimeline.HasPausedEvents()
    timeline.hasVisibleEvents = C_EncounterTimeline.HasVisibleEvents()
end

function Fuyutsui:RefreshEncounterTimelineTracks()
    if not C_EncounterTimeline then
        return
    end
    self.encounterTimeline.tracks = C_EncounterTimeline.GetTrackList() or {}
end

function Fuyutsui:RefreshEncounterTimelineEvent(eventID)
    local record = self.encounterTimeline.events[eventID]
    if not record or not C_EncounterTimeline then
        return nil
    end

    local state = C_EncounterTimeline.GetEventState(eventID)
    if state ~= nil and not isSec(state) then
        record.state = state
    end

    local elapsed = C_EncounterTimeline.GetEventTimeElapsed(eventID)
    if elapsed ~= nil and not isSec(elapsed) then
        record.timeElapsed = elapsed
    end

    local remaining = C_EncounterTimeline.GetEventTimeRemaining(eventID)
    if remaining ~= nil and not isSec(remaining) then
        record.timeRemaining = remaining
        AnchorTiming(record)
    end

    local track, trackSortIndex = C_EncounterTimeline.GetEventTrack(eventID)
    if track ~= nil and not isSec(track) then
        record.track = track
    end
    if trackSortIndex ~= nil and not isSec(trackSortIndex) then
        record.trackSortIndex = trackSortIndex
    else
        record.trackSortIndex = nil
    end

    local blocked = C_EncounterTimeline.IsEventBlocked(eventID)
    if blocked ~= nil and not isSec(blocked) then
        record.blocked = blocked
    end

    local color = C_EncounterTimeline.GetEventColor(eventID)
    if color ~= nil then
        eventColors[eventID] = color
    end

    return record
end

function Fuyutsui:GetEncounterTimelineTimeRemaining(eventID)
    local record = self.encounterTimeline.events[eventID]
    if not record or record.timeRemaining == nil then return nil end
    if record.timingPaused or record.anchorTime == nil then return record.timeRemaining end
    return math.max(0, record.timeRemaining - (GetTime() - record.anchorTime))
end

function Fuyutsui:RefreshEncounterTimelinePixels()
    local start = self.blocks.encounterTimeline
    if not start then return end

    local sorted = {}
    for eventID, record in pairs(self.encounterTimeline.events) do
        local remaining = self:GetEncounterTimelineTimeRemaining(eventID)
        if remaining ~= nil then
            sorted[#sorted + 1] = { id = eventID, record = record, remaining = remaining }
        end
    end
    -- 不读取秘密 spellID 作为排序条件；同一剩余时间用公开事件 ID 保持顺序稳定。
    table.sort(sorted, function(a, b)
        if a.remaining ~= b.remaining then return a.remaining < b.remaining end
        return a.id < b.id
    end)

    for slot = 1, self.EncounterTimelineSlotCount do
        local index = start + (slot - 1) * 3
        local event = sorted[slot]
        if event then
            self:RefreshRgbNumberPixel(index, event.record.spellID)
            self:CreateTexture(index + 2, TimePixel(event.remaining))
        else
            self:ClearRgbSpellPixel(index)
            self:CreateNumberTexture(index, 0)
            self:CreateTexture(index + 2, 0)
        end
    end
end

function Fuyutsui:GetEncounterTimelineTimeElapsed(eventID)
    local record = self.encounterTimeline.events[eventID]
    if not record or record.timeElapsed == nil then
        return nil
    end
    if record.timingPaused or record.anchorTime == nil then
        return record.timeElapsed
    end
    return record.timeElapsed + (GetTime() - record.anchorTime)
end

function Fuyutsui:GetShortestEncounterTimelineEvent()
    local shortestID, shortestRemaining
    for eventID, record in pairs(self.encounterTimeline.events) do
        if record.state ~= STATE_FINISHED and record.state ~= STATE_CANCELED then
            local remaining = self:GetEncounterTimelineTimeRemaining(eventID)
            if remaining ~= nil and (shortestRemaining == nil or remaining < shortestRemaining) then
                shortestID = eventID
                shortestRemaining = remaining
            end
        end
    end
    return shortestID, shortestRemaining
end

function Fuyutsui:AddEncounterTimelineEvent(eventInfo)
    if not eventInfo then
        return nil
    end

    local eventID = eventInfo.id
    if eventID == nil or isSec(eventID) or eventID == 0 then
        return nil
    end

    local events = self.encounterTimeline.events
    local record = events[eventID]
    if not record then
        record = CreateRecord(eventInfo)
        events[eventID] = record
    end

    self:RefreshEncounterTimelineEvent(eventID)
    return record
end

function Fuyutsui:RemoveEncounterTimelineEvent(eventID)
    if eventID == nil or isSec(eventID) then
        return
    end

    self.encounterTimeline.events[eventID] = nil
    eventColors[eventID] = nil
    self:RefreshEncounterTimelinePixels()
end

function Fuyutsui:ClearEncounterTimelineEvents()
    wipe(self.encounterTimeline.events)
    wipe(eventColors)
    self:RefreshEncounterTimelinePixels()
end

local function TouchEncounterTimelineEvent(self, eventID)
    if eventID == nil or isSec(eventID) or eventID == 0 then
        return nil
    end
    if self.encounterTimeline.events[eventID] then
        return self:RefreshEncounterTimelineEvent(eventID)
    end
    if not C_EncounterTimeline then
        return nil
    end

    local eventInfo = C_EncounterTimeline.GetEventInfo(eventID)
    if not eventInfo then
        return nil
    end
    return self:AddEncounterTimelineEvent(eventInfo)
end

function Fuyutsui:SyncEncounterTimeline()
    self:RefreshEncounterTimelineStatus()
    if not C_EncounterTimeline or not self.encounterTimeline.available then
        self:ClearEncounterTimelineEvents()
        self.encounterTimeline.tracks = {}
        self.encounterTimeline.viewActive = false
        return
    end

    self:RefreshEncounterTimelineTracks()

    local seen = {}
    local eventIDs = C_EncounterTimeline.GetEventList()
    if eventIDs then
        for _, eventID in ipairs(eventIDs) do
            if eventID ~= nil and not isSec(eventID) and eventID ~= 0 then
                seen[eventID] = true
                local eventInfo = C_EncounterTimeline.GetEventInfo(eventID)
                if eventInfo then
                    self:AddEncounterTimelineEvent(eventInfo)
                end
            end
        end
    end

    for eventID in pairs(self.encounterTimeline.events) do
        if not seen[eventID] then
            self.encounterTimeline.events[eventID] = nil
            eventColors[eventID] = nil
        end
    end
end

function Fuyutsui:EnableEncounterTimeline()
    if self.encounterTimelineRegistered or not C_EncounterTimeline then
        return
    end
    self.encounterTimelineRegistered = true

    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_ADDED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_REMOVED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_TRACK_CHANGED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_BLOCK_STATE_CHANGED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_COLOR_CHANGED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_HIGHLIGHT")
    self:RegisterEvent("ENCOUNTER_TIMELINE_LAYOUT_UPDATED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_STATE_UPDATED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_VIEW_ACTIVATED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_VIEW_DEACTIVATED")
    self:SyncEncounterTimeline()
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_ADDED(_, eventInfo)
    self:AddEncounterTimelineEvent(eventInfo)
    self:RefreshEncounterTimelineStatus()
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_REMOVED(_, eventID)
    -- 此事件在实例删除之后触发，不能再按 eventID 查询。
    self:RemoveEncounterTimelineEvent(eventID)
    self:RefreshEncounterTimelineStatus()
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED(_, eventID)
    TouchEncounterTimelineEvent(self, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_TRACK_CHANGED(_, eventID)
    TouchEncounterTimelineEvent(self, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_BLOCK_STATE_CHANGED(_, eventID)
    TouchEncounterTimelineEvent(self, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_COLOR_CHANGED(_, eventID)
    TouchEncounterTimelineEvent(self, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_HIGHLIGHT(_, eventID)
    local record = TouchEncounterTimelineEvent(self, eventID)
    if record then
        record.highlighted = true
    end
end

function Fuyutsui:ENCOUNTER_TIMELINE_LAYOUT_UPDATED()
    self:RefreshEncounterTimelineStatus()
    self:RefreshEncounterTimelineTracks()
end

function Fuyutsui:ENCOUNTER_TIMELINE_STATE_UPDATED()
    self:RefreshEncounterTimelineStatus()
end

function Fuyutsui:ENCOUNTER_TIMELINE_VIEW_ACTIVATED(_, viewType)
    self:SyncEncounterTimeline()
    local timeline = self.encounterTimeline
    if viewType ~= nil and not isSec(viewType) then
        timeline.viewType = viewType
    end
    timeline.viewActive = true
end

function Fuyutsui:ENCOUNTER_TIMELINE_VIEW_DEACTIVATED(_, viewType)
    self:ClearEncounterTimelineEvents()
    local timeline = self.encounterTimeline
    if viewType ~= nil and not isSec(viewType) then
        timeline.viewType = viewType
    end
    timeline.viewActive = false
    self:RefreshEncounterTimelineStatus()
end
