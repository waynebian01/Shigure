local addon, ns = ...

local state = Fuyutsui.state
local MAX_COUNTDOWN_SECONDS = 255
local TIMER_EXPIRE_GRACE_SECONDS = 1.5
local NOTICE_SECONDS = 1.0

-- 类型像素：0=无/未安装，1=精确计时，2=近似CD，3=点名计时，4=施法计时，5=即时警告。
local TYPE_EXACT_TIMER = 1
local TYPE_APPROXIMATE_CD = 2
local TYPE_TARGET_TIMER = 3
local TYPE_CAST_TIMER = 4
local TYPE_MESSAGE = 5

-- 与 Shigure 内嵌目录共用：按 spellID 升序分配一基事件键。
local EVENT_SPELL_IDS = {
    1221622, 1221637, 1221781, 1221787, 1222088, 1232467, 1233602, 1233787, 1233865, 1237038,
    1237614, 1237837, 1238843, 1239080, 1241282, 1241292, 1241313, 1241692, 1242260, 1242515,
    1242981, 1243743, 1243753, 1243982, 1244221, 1244344, 1244672, 1244917, 1245391, 1245396,
    1245406, 1245486, 1245645, 1246162, 1246175, 1246461, 1246485, 1246621, 1246653, 1246709,
    1246736, 1246749, 1246765, 1246918, 1247738, 1248449, 1248451, 1248644, 1248674, 1248697,
    1248710, 1248983, 1249251, 1249262, 1249609, 1249620, 1249748, 1250686, 1250803, 1250898,
    1251361, 1251386, 1251857, 1253915, 1254081, 1254199, 1255368, 1255738, 1256855, 1257085,
    1257087, 1257717, 1258610, 1258668, 1258883, 1260052, 1260712, 1260763, 1260837, 1261016,
    1261339, 1262036, 1262289, 1262623, 1264756, 1265131, 1266388, 1266897, 1267049, 1268562,
    1272726, 1273158, 1276243, 1276525, 1276710, 1277025, 1279420, 1280015, 1280458, 1280935,
    1281194, 1281907, 1282117, 1282281, 1282412, 1282441, 1282469, 1282487, 1282525, 1282937,
    1283164, 1283489, 1283832, 1284251, 1284434, 1284458, 1284483, 1284487, 1284525, 1284588,
    1284931, 1284980, 1285425, 1285681, 1285732, 1285911, 1286441, 1286573, 1286860, 1286895,
    1286905, 1286918, 1286921, 1287426, 1287533, 1288232, 1288538, 1289192, 1289900, 1290516,
    1290711, 1290779, 1290809, 1290956, 1291390, 1291404, 1291478, 1291759, 1291933, 1292036,
    1292104, 1292188, 1292779, 1292999, 1293212, 1294293, 1295397, 1295817, 1295854, 1295886,
    1295905, 1296092, 1296249, 1296301, 1296535, 1296878, 1296898, 1297022, 1298367, 1298381,
    1298559, 1299266, 1299673, 1299680, 1299757, 1299960, 1300530, 1300635, 1300751, 1301117,
    1301213, 1301510, 1302982, 1303230, 1305421, 1305959, 1306872, 1307279, 1308356, 1308556,
    1310738, 1310763, 1313393,
}

local eventKeyBySpellID = {}
for key, spellID in ipairs(EVENT_SPELL_IDS) do
    if key <= 255 then eventKeyBySpellID[spellID] = key end
end
Fuyutsui.BigWigsEventSpellIDs = EVENT_SPELL_IDS
Fuyutsui.BigWigsEventKeyBySpellID = eventKeyBySpellID

local STRING_KEY_ALIASES = {
    empowered_avengers_shield = 1246485,
    empowered_divine_storm = 1246765,
    empowered_searing_radiance = 1255738,
}

local activeTimers = {}
local pendingEventIDs = {}
local bridgeOwner = {}
local currentModule = nil

local function SafeNumber(value)
    local ok, number = pcall(tonumber, value)
    return ok and number or nil
end

local function GetNow()
    return GetTime and GetTime() or 0
end

local function ModuleIdentity(module)
    if type(module) ~= "table" then return "unknown" end
    local encounterID = type(module.GetEncounterID) == "function" and SafeNumber(module:GetEncounterID()) or nil
    return tostring(encounterID or module.moduleName or module.name or "unknown")
end

local function IsRaidBossModule(module)
    if type(module) ~= "table" then return false end
    local _, instanceType = GetInstanceInfo()
    if instanceType ~= "raid" then return false end
    if type(module.IsTrashModule) == "function" and module:IsTrashModule() then return false end
    if module.isLittleWigs == true then return false end

    local encounterID = type(module.GetEncounterID) == "function" and SafeNumber(module:GetEncounterID()) or nil
    local currentEncounterID = SafeNumber(state.encounterID)
    return not (encounterID and currentEncounterID and currentEncounterID > 0
        and encounterID ~= currentEncounterID)
end

local function ResolveSpellID(key, icon)
    local spellID = SafeNumber(key)
    if spellID and eventKeyBySpellID[spellID] then return spellID end
    if type(key) == "string" and STRING_KEY_ALIASES[key] then return STRING_KEY_ALIASES[key] end
    spellID = SafeNumber(icon)
    if spellID and eventKeyBySpellID[spellID] then return spellID end
    return nil
end

local function TimerIdentity(module, key, text)
    return ModuleIdentity(module) .. "|" .. tostring(key or "") .. "|" .. tostring(text or "")
end

local function AddTimer(module, key, duration, text, icon, typeCode, forceCountdown)
    if not IsRaidBossModule(module) then return end
    local seconds = SafeNumber(duration)
    if not seconds or seconds < 0 then return end

    local spellID = ResolveSpellID(key, icon)
    local timerKey = TimerIdentity(module, key, text)
    if forceCountdown ~= nil then
        timerKey = timerKey .. "|notice:" .. tostring(GetNow())
    end
    activeTimers[timerKey] = {
        module = module,
        eventKey = spellID and (eventKeyBySpellID[spellID] or 0) or 0,
        typeCode = typeCode,
        text = text,
        eventID = pendingEventIDs[timerKey],
        castTime = GetNow() + seconds,
        forceCountdown = forceCountdown,
    }
    pendingEventIDs[timerKey] = nil
end

local function ClearModule(module)
    if not module then
        wipe(activeTimers)
        wipe(pendingEventIDs)
        return
    end
    for key, timer in pairs(activeTimers) do
        if timer.module == module then activeTimers[key] = nil end
    end
    local prefix = ModuleIdentity(module) .. "|"
    for key in pairs(pendingEventIDs) do
        if key:sub(1, #prefix) == prefix then pendingEventIDs[key] = nil end
    end
end

local function OnStartBar(_, module, key, text, time, icon, isApprox, maxTime, eventID)
    if IsRaidBossModule(module) and eventID then
        pendingEventIDs[TimerIdentity(module, key, text)] = eventID
    end
end

local function OnTimer(_, module, key, time, maxTime, text, counter, icon, isApprox)
    AddTimer(module, key, time, text, icon, isApprox == true and TYPE_APPROXIMATE_CD or TYPE_EXACT_TIMER)
end

local function OnTargetTimer(_, module, key, time, maxTime, text, counter, icon)
    AddTimer(module, key, time, text, icon, TYPE_TARGET_TIMER)
end

local function OnCastTimer(_, module, key, time, maxTime, text, counter, icon)
    AddTimer(module, key, time, text, icon, TYPE_CAST_TIMER)
end

local function OnMessage(_, module, key, text, color, icon)
    if ResolveSpellID(key, icon) then
        AddTimer(module, key, NOTICE_SECONDS, text, icon, TYPE_MESSAGE, 0)
    end
end

local function OnStopBar(_, module, text, eventID)
    for key, timer in pairs(activeTimers) do
        if (module == nil or timer.module == module)
            and ((eventID ~= nil and timer.eventID == eventID)
                or (text ~= nil and timer.text == text)) then
            activeTimers[key] = nil
        end
    end
end

local function OnStopBars(_, module)
    ClearModule(module)
end

local function OnBossEngage(_, module)
    wipe(activeTimers)
    wipe(pendingEventIDs)
    currentModule = module
end

local function OnBossFinished(_, module)
    ClearModule(module or currentModule)
    if module == nil or module == currentModule then currentModule = nil end
end

local STATE_KEYS = { "bigWigsBossSkillType", "bigWigsBossSkillEvent", "bigWigsBossSkillCountdown" }
local FIELD_NAMES = { "BigWigs首领技能类型", "BigWigs首领技能事件", "BigWigs首领技能倒计时" }

local function SetPixelStates(typeCode, eventKey, countdownSeconds)
    typeCode = math.max(0, math.min(5, math.floor(SafeNumber(typeCode) or 0)))
    eventKey = math.max(0, math.min(255, math.floor(SafeNumber(eventKey) or 0)))
    countdownSeconds = math.max(0, math.min(MAX_COUNTDOWN_SECONDS,
        math.floor((SafeNumber(countdownSeconds) or 0) + 0.5)))

    local values = { typeCode / 255, eventKey / 255, countdownSeconds / 255 }
    for index = 1, 3 do
        if state[STATE_KEYS[index]] ~= values[index] then
            state[STATE_KEYS[index]] = values[index]
            Fuyutsui:UpdateStateBlock("特殊", FIELD_NAMES[index])
        end
    end
end

function Fuyutsui:RefreshBigWigsTimelinePixels()
    if not self.bigWigsTimelineBridgeInitialized then self:InitializeBigWigsTimelineBridge() end

    local now = GetNow()
    local selected, selectedRemaining
    for key, timer in pairs(activeTimers) do
        local remaining = timer.castTime and (timer.castTime - now) or nil
        if not remaining or remaining < -TIMER_EXPIRE_GRACE_SECONDS then
            activeTimers[key] = nil
        elseif not selectedRemaining or remaining < selectedRemaining then
            selected, selectedRemaining = timer, remaining
        end
    end

    if not selected then
        SetPixelStates(0, 0, 0)
        return
    end

    local countdown = selected.forceCountdown
    if countdown == nil then countdown = selectedRemaining > 0 and math.ceil(selectedRemaining) or 0 end
    SetPixelStates(selected.typeCode, selected.eventKey, countdown)
end

function Fuyutsui:ResetBigWigsTimelinePixels()
    wipe(activeTimers)
    wipe(pendingEventIDs)
    currentModule = nil
    SetPixelStates(0, 0, 0)
end

function Fuyutsui:InitializeBigWigsTimelineBridge()
    if self.bigWigsTimelineBridgeInitialized then return true end
    local loader = _G.BigWigsLoader
    if not loader or type(loader.RegisterMessage) ~= "function" then
        SetPixelStates(0, 0, 0)
        return false
    end

    loader.RegisterMessage(bridgeOwner, "BigWigs_StartBar", OnStartBar)
    loader.RegisterMessage(bridgeOwner, "BigWigs_Timer", OnTimer)
    loader.RegisterMessage(bridgeOwner, "BigWigs_TargetTimer", OnTargetTimer)
    loader.RegisterMessage(bridgeOwner, "BigWigs_CastTimer", OnCastTimer)
    loader.RegisterMessage(bridgeOwner, "BigWigs_Message", OnMessage)
    loader.RegisterMessage(bridgeOwner, "BigWigs_StopBar", OnStopBar)
    loader.RegisterMessage(bridgeOwner, "BigWigs_StopBars", OnStopBars)
    loader.RegisterMessage(bridgeOwner, "BigWigs_OnBossEngage", OnBossEngage)
    loader.RegisterMessage(bridgeOwner, "BigWigs_OnBossEngageMidEncounter", OnBossEngage)
    loader.RegisterMessage(bridgeOwner, "BigWigs_OnBossWin", OnBossFinished)
    loader.RegisterMessage(bridgeOwner, "BigWigs_OnBossWipe", OnBossFinished)
    loader.RegisterMessage(bridgeOwner, "BigWigs_OnBossDisable", OnBossFinished)

    self.bigWigsTimelineBridgeInitialized = true
    return true
end
