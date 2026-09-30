local addon, ns = ...
local className, classFilename, classId = UnitClass("player")

Shingen = Shingen or {}

local eventFrame = CreateFrame("Frame", "ShingenEventFrame")

--- 与 AceEvent 一致：回调为 addon[event](addon, event, ...)
function Shingen:RegisterEvent(event)
    eventFrame:RegisterEvent(event)
end

function Shingen:UnregisterEvent(event)
    eventFrame:UnregisterEvent(event)
end

function Shingen:UnregisterAllEvents()
    eventFrame:UnregisterAllEvents()
end

local function CopyDefaults(dst, src)
    if type(dst) ~= "table" or type(src) ~= "table" then
        return
    end
    for k, v in pairs(src) do
        if type(v) == "table" then
            if type(dst[k]) ~= "table" then
                dst[k] = {}
            end
            CopyDefaults(dst[k], v)
        elseif dst[k] == nil then
            dst[k] = v
        end
    end
end

local function GetCharKey()
    local name = UnitName("player") or "Unknown"
    local realm = GetRealmName() or "Unknown"
    return name .. " - " .. realm
end

--- 兼容原 AceDB-3.0 的 ShingenADB 布局（char / profiles / profileKeys）
local function InitDB()
    if type(ShingenADB) ~= "table" then
        ShingenADB = {}
    end
    local sv = ShingenADB
    sv.char = sv.char or {}
    sv.profiles = sv.profiles or {}
    sv.profileKeys = sv.profileKeys or {}

    local charKey = GetCharKey()
    if type(sv.char[charKey]) ~= "table" then
        sv.char[charKey] = {}
    end
    local char = sv.char[charKey]
    CopyDefaults(char, Shingen.defaults.char)

    local profileName = sv.profileKeys[charKey] or "Default"
    sv.profileKeys[charKey] = profileName
    if type(sv.profiles[profileName]) ~= "table" then
        sv.profiles[profileName] = {}
    end
    local profile = sv.profiles[profileName]
    CopyDefaults(profile, Shingen.defaults.profile)

    Shingen.db = {
        char = char,
        profile = profile,
    }
end

function Shingen:OnInitialize()
    InitDB()

    SLASH_SHINGEN1 = "/sh"
    SLASH_SHINGEN2 = "/shingen"
    SlashCmdList["SHINGEN"] = function(msg, editbox)
        Shingen:SlashCommand(msg, editbox)
    end

    self:InitializeCharacterState()
end

function Shingen:OnEnable()
    self:InitializeSpecializationState()
    if self.InitializeExBossTimelineBridge then
        self:InitializeExBossTimelineBridge()
    end
    if self.InitializeBigWigsTimelineBridge then
        self:InitializeBigWigsTimelineBridge()
    end
    self:UpdateSpellKnown()
    self:RefreshPlayerState()
    self:ReadKeybindings()
    self:HookChatFrameEditBox()

    self:RegisterEvent("ZONE_CHANGED")
    self:RegisterEvent("ZONE_CHANGED_INDOORS")
    self:RegisterEvent("PLAYER_ENTERING_WORLD")
    self:RegisterEvent("PLAYER_TALENT_UPDATE")
    self:RegisterEvent("PLAYER_DEAD")
    self:RegisterEvent("PLAYER_ALIVE")
    self:RegisterEvent("PLAYER_UNGHOST")
    self:RegisterEvent("PLAYER_MOUNT_DISPLAY_CHANGED")
    self:RegisterEvent("UNIT_PET")
    self:RegisterEvent("PLAYER_REGEN_DISABLED")
    self:RegisterEvent("PLAYER_REGEN_ENABLED")
    self:RegisterEvent("PLAYER_STARTED_MOVING")
    self:RegisterEvent("PLAYER_STOPPED_MOVING")
    self:RegisterEvent("UNIT_SPELLCAST_SENT")
    self:RegisterEvent("UNIT_SPELLCAST_START")
    self:RegisterEvent("UNIT_SPELLCAST_STOP")
    self:RegisterEvent("UNIT_SPELLCAST_INTERRUPTED")
    self:RegisterEvent("UNIT_SPELLCAST_CHANNEL_START")
    self:RegisterEvent("UNIT_SPELLCAST_CHANNEL_STOP")
    self:RegisterEvent("UNIT_SPELLCAST_EMPOWER_START")
    self:RegisterEvent("UNIT_SPELLCAST_EMPOWER_STOP")
    self:RegisterEvent("UNIT_SPELLCAST_SUCCEEDED")
    self:RegisterEvent("UNIT_POWER_UPDATE")
    self:RegisterEvent("UNIT_MAXPOWER")
    self:RegisterEvent("UNIT_POWER_POINT_CHARGE")
    self:RegisterEvent("UNIT_HEALTH")
    self:RegisterEvent("UNIT_MAXHEALTH")
    self:RegisterEvent("UNIT_HEAL_ABSORB_AMOUNT_CHANGED")
    self:RegisterEvent("UNIT_HEAL_PREDICTION")
    self:RegisterEvent("SPELL_UPDATE_USES")
    self:RegisterEvent("GROUP_ROSTER_UPDATE")
    self:RegisterEvent("UNIT_DIED")
    self:RegisterEvent("SPELL_RANGE_CHECK_UPDATE")
    self:RegisterEvent("ACTION_RANGE_CHECK_UPDATE")
    self:RegisterEvent("UI_ERROR_MESSAGE")
    self:RegisterEvent("PLAYER_TARGET_CHANGED")
    self:RegisterEvent("PLAYER_FOCUS_CHANGED")
    self:RegisterEvent("UPDATE_MOUSEOVER_UNIT")
    self:RegisterEvent("NAME_PLATE_UNIT_ADDED")
    self:RegisterEvent("NAME_PLATE_UNIT_REMOVED")
    self:RegisterEvent("UNIT_THREAT_SITUATION_UPDATE")
    self:RegisterEvent("UPDATE_SHAPESHIFT_FORM")
    self:RegisterEvent("UPDATE_SHAPESHIFT_FORMS")
    self:RegisterEvent("ENCOUNTER_START")
    self:RegisterEvent("ENCOUNTER_END")
    self:RegisterEvent("INSTANCE_ENCOUNTER_ENGAGE_UNIT")
    self:RegisterEvent("SPELL_UPDATE_COOLDOWN")
    self:RegisterEvent("UPDATE_BINDINGS")
    self:RegisterEvent("SPELLS_CHANGED")
    self:RegisterEvent("ACTIONBAR_HIDEGRID")
    self:RegisterEvent("ACTIONBAR_SHOWGRID")
    self:RegisterEvent("SPELL_UPDATE_CHARGES")
    self:RegisterEvent("SPELL_UPDATE_ICON")
    self:RegisterEvent("ITEM_COUNT_CHANGED")
    self:RegisterEvent("BAG_UPDATE")
    self:RegisterEvent("PLAYERBANKSLOTS_CHANGED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_ADDED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_REMOVED")
    self:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED")
    self:RegisterEvent("UNIT_AURA")
    self:RegisterEvent("CINEMATIC_STOP")
    self:RegisterEvent("STOP_MOVIE")
    self:RegisterEvent("START_PLAYER_COUNTDOWN")
    self:RegisterEvent("CANCEL_PLAYER_COUNTDOWN")
    if self.StartFrameUpdates then
        self:StartFrameUpdates()
    end
    if self.InitQuickToggleButton then
        self:InitQuickToggleButton()
    end
    self.isInitialized = true
end

Shingen.state = {
    classId = classId,
    className = className,
    classFilename = classFilename,
}

local function CreateUnitState()
    return {
        casting = false,
        channeling = false,
        empowering = false,
    }
end

Shingen.blocks = {}
Shingen.target = CreateUnitState()
Shingen.focus = CreateUnitState()
Shingen.mouseover = CreateUnitState()
Shingen.pet = CreateUnitState()
Shingen.boss = {}
for index = 1, 5 do
    Shingen.boss["boss" .. index] = CreateUnitState()
end
Shingen.group = {}
Shingen.groupList = {}
Shingen.defaults = {
    profile = {
        someInput = "",
    },
    char = {
        level = 0,
        aoeMode = 0,
        cooldowns = 0,
        dpsMode = 0,
        delay = 0,
        potion = 0,
        quickButtonCX = 180,
        quickButtonCY = -100,
        quickButtonShow = true,
    },
}

local initialized = false
local enabled = false

eventFrame:RegisterEvent("ADDON_LOADED")
eventFrame:RegisterEvent("PLAYER_LOGIN")
eventFrame:SetScript("OnEvent", function(_, event, ...)
    if event == "ADDON_LOADED" then
        local name = ...
        if name ~= addon then
            return
        end
        eventFrame:UnregisterEvent("ADDON_LOADED")
        if not initialized then
            initialized = true
            Shingen:OnInitialize()
        end
        if IsLoggedIn() and not enabled then
            enabled = true
            Shingen:OnEnable()
        end
        return
    end

    if event == "PLAYER_LOGIN" then
        eventFrame:UnregisterEvent("PLAYER_LOGIN")
        if not enabled then
            enabled = true
            Shingen:OnEnable()
        end
        return
    end

    local handler = Shingen[event]
    if handler then
        handler(Shingen, event, ...)
    end
end)
