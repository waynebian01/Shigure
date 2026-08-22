local addon, ns = ...
local screenWidth = GetScreenWidth()

--[[============================================================================
    可修改配置（置顶）
============================================================================]]

-- 主色条（FuyutsuiColorBars / CreateTexture）
local BLOCK_FIX_COUNT = 510        -- 总色块数量
local BLOCK_FIRST_SCHEME_MAX = 255 -- 第一套索引方案上限（其后用 r=1/255）
local BLOCK_HEIGHT = 1             -- 色块高度
local BLOCK_SPACING = 0            -- 色块间距
local COLOR_BARS_STRATA = "TOOLTIP"
local COLOR_BARS_LEVEL = 9001

-- 横向条（FuyutsuiCountBars：计数条 + 光环层数条，BAR_END_COLOR 收尾）
local BAR_UNIT_COUNT = 500  -- 横向单元数
local BAR_HEIGHT = 2        -- 条高度
local BAR_FRAME_HEIGHT = 20 -- 容器高度
local BAR_START_INDEX = 2   -- 首条占用起始单元
local BAR_STRATA = "TOOLTIP"
local BAR_LEVEL = 1
local BAR_STATUS_LEVEL = 8999                                -- StatusBar 层级
local BAR_END_COLOR = { 200 / 255, 200 / 255, 200 / 255, 1 } -- 全部条之后的终点色块

-- AuraContainer 计时色块（█）
local AURA_BLOCK_HEIGHT = BLOCK_HEIGHT -- 高单独设置；宽与主色块一致
local AURA_DURATION_CHAR = "█"
local AURA_ENABLE_MOUSE = false        -- false = 关闭悬停提示
local AURA_DURATION_STRATA = "TOOLTIP"
local AURA_DURATION_LEVEL = 9003

-- AuraContainer 层数条
local AURA_BAR_STRATA = "TOOLTIP"
local AURA_BAR_LEVEL = 9004

-- 队伍治疗吸收条（FuyutsuiHealAbsorbBars）
local HEAL_ABSORB_MAX_SLOTS = 30  -- 最大槽位数
local HEAL_ABSORB_COLS = 5        -- 每行列数
local HEAL_ABSORB_BAR_UNITS = 100 -- 单条条身单元数
local HEAL_ABSORB_WIDTH_SCALE = 0.7 -- 单元宽度相对横向条的缩放比例

--[[============================================================================
    派生尺寸（一般不用改）
============================================================================]]

local BLOCK_FIX_CONFIG = {
    blockCount = BLOCK_FIX_COUNT,
    blockWidth = screenWidth / BLOCK_FIX_COUNT,
    blockHeight = BLOCK_HEIGHT,
    blockSpacing = BLOCK_SPACING,
}

local BAR_CONFIG = {
    count = BAR_UNIT_COUNT,
    heightOffset = -BLOCK_HEIGHT,
    width = screenWidth / BAR_UNIT_COUNT,
    height = BAR_HEIGHT,
    point = "TOPLEFT",
}

local HEAL_ABSORB_SLOT_UNITS = 1 + HEAL_ABSORB_BAR_UNITS + 1 -- 前锚点 + 条身 + 终点
local HEAL_ABSORB_ROWS = HEAL_ABSORB_MAX_SLOTS / HEAL_ABSORB_COLS
local HEAL_ABSORB_UNIT_WIDTH = BAR_CONFIG.width * HEAL_ABSORB_WIDTH_SCALE

local AURA_BLOCK_W = BLOCK_FIX_CONFIG.blockWidth
local AURA_BLOCK_H = AURA_BLOCK_HEIGHT

--- 索引 1..255 → r=0, g=i/255；256..510 → r=1/255, g=(i-255)/255
local function EncodeBlockChannels(index)
    if index > BLOCK_FIRST_SCHEME_MAX then
        return 1 / 255, (index - BLOCK_FIRST_SCHEME_MAX) / 255
    end
    return 0, index / 255
end

local function EnsureAuraContainerLoaded()
    if C_AddOns and not C_AddOns.IsAddOnLoaded("Blizzard_AuraContainer") then
        C_AddOns.LoadAddOn("Blizzard_AuraContainer")
    end
end

--[[============================================================================
    主色条
============================================================================]]

local function GetXOffset(index, Width, spacing)
    return index * (Width + spacing)
end

local colorBars = CreateFrame("Frame", "FuyutsuiColorBars", UIParent)
colorBars:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
colorBars:SetSize(screenWidth, BLOCK_FIX_CONFIG.blockHeight)
colorBars:SetFrameStrata(COLOR_BARS_STRATA)
colorBars:SetFrameLevel(COLOR_BARS_LEVEL)

local pixelTextures = {}

local function createTextureByIndex(i)
    if i <= 0 or i > BLOCK_FIX_CONFIG.blockCount then return nil end
    if pixelTextures[i] == nil then
        local tex = colorBars:CreateTexture(nil, "OVERLAY")
        tex:SetSize(BLOCK_FIX_CONFIG.blockWidth, BLOCK_FIX_CONFIG.blockHeight)
        tex:SetPoint("TOPLEFT", colorBars, "TOPLEFT",
                     GetXOffset(i - 1, BLOCK_FIX_CONFIG.blockWidth, BLOCK_FIX_CONFIG.blockSpacing), 0)
        pixelTextures[i] = tex
    end
    return pixelTextures[i]
end

-- 索引 1..255: (0, i/255, b, 1)；索引 256..510: (1/255, (i-255)/255, b, 1)
function Fuyutsui:CreateTexture(i, b)
    local tex = createTextureByIndex(i)
    if tex then
        local r, g = EncodeBlockChannels(i)
        tex:SetColorTexture(r, g, b, 1)
    end
end

function Fuyutsui:ClearAllTextures()
    for i = 1, BLOCK_FIX_CONFIG.blockCount do
        self:CreateTexture(i, 0)
    end
end

for i = 1, BLOCK_FIX_CONFIG.blockCount do
    Fuyutsui:CreateTexture(i, 0)
end

--[[============================================================================
    横向计数条布局（计数条 + AuraContainer 层数条共用）
    排布：计数条 → 光环层数条 → BAR_END_COLOR（终点色块始终在最后）
    单条占用：背景单元 [-1..max] + 预留终点位 + 间隔 → 步进 max+3
============================================================================]]

local countBars = CreateFrame("Frame", "FuyutsuiCountBars", UIParent)
countBars:SetSize(screenWidth, BAR_FRAME_HEIGHT)
countBars:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, BAR_CONFIG.heightOffset)
countBars:SetFrameStrata(BAR_STRATA)
countBars:SetFrameLevel(BAR_LEVEL)

local createdBars = {}
local spellIdToBar = {}
local nextAvailableIndex = BAR_START_INDEX
-- 计数条排完后的 nextAvailableIndex；层数条从此处起排，释放后回退到这里避免错位
local countBarLayoutEndIndex = BAR_START_INDEX
local countBarEndTexture = nil
local auraBarLaidOut = false
local anyHorizontalBarLaidOut = false

local BAR_EVENTS = { "SPELL_UPDATE_USES", "PLAYER_ENTERING_WORLD", "SPELL_UPDATE_CHARGES" }
local UNIT_AURA_REBIND_ORDER = { "player", "target", "focus" }

--- 预留一条横向条的单元；成功返回 startIndex，空间不足返回 nil
local function ReserveHorizontalBarUnits(maxValue, warnMsg)
    local startIndex = nextAvailableIndex
    -- +1 终点色块预留，+2 与下一条间隔（终点色块最终只画在全部条之后）
    local newIndex = startIndex + maxValue + 3
    if newIndex > BAR_CONFIG.count then
        if warnMsg then
            print(warnMsg)
        end
        return nil
    end
    nextAvailableIndex = newIndex
    anyHorizontalBarLaidOut = true
    return startIndex
end

--- 背景索引色块：(r=1/255, g=相对索引/255, b=0)，供外部定位条段
local function CreateHorizontalBarBackgrounds(startIndex, maxValue)
    for i = -1, maxValue do
        local currentRelativeIndex = i + 1
        local absolutePos = startIndex + i
        local tex = countBars:CreateTexture(nil, "BACKGROUND")
        tex:SetSize(BAR_CONFIG.width, BAR_CONFIG.height)
        tex:SetPoint("TOPLEFT", countBars, "TOPLEFT", (absolutePos - 1) * BAR_CONFIG.width, 0)
        tex:SetColorTexture(1 / 255, currentRelativeIndex / 255, 0, 1)
    end
end

local function StyleHorizontalStatusBar(bar)
    bar:SetStatusBarTexture("Interface\\ChatFrame\\ChatFrameBackground")
    bar:GetStatusBarTexture():SetDrawLayer("ARTWORK")
    bar:SetStatusBarColor(1, 1, 1, 1)
end

--- 将终点色块放到当前已分配内容之后（nextAvailableIndex - 2）
local function UpdateHorizontalBarEndMarker()
    if not anyHorizontalBarLaidOut then
        if countBarEndTexture then
            countBarEndTexture:Hide()
        end
        return
    end
    local endPos = nextAvailableIndex - 2
    if not countBarEndTexture then
        countBarEndTexture = countBars:CreateTexture(nil, "BACKGROUND")
        countBarEndTexture:SetSize(BAR_CONFIG.width, BAR_CONFIG.height)
    end
    countBarEndTexture:ClearAllPoints()
    countBarEndTexture:SetPoint("TOPLEFT", countBars, "TOPLEFT", (endPos - 1) * BAR_CONFIG.width, 0)
    countBarEndTexture:SetColorTexture(BAR_END_COLOR[1], BAR_END_COLOR[2], BAR_END_COLOR[3], BAR_END_COLOR[4])
    countBarEndTexture:Show()
end

---@param minValue number
---@param maxValue number
---@param spellId number
function Fuyutsui:CreateAutoLayoutBar(valueType, minValue, maxValue, spellId)
    maxValue = maxValue or 0
    minValue = minValue or 0
    if spellIdToBar[spellId] then
        return spellIdToBar[spellId]
    end

    local startIndex = ReserveHorizontalBarUnits(maxValue, "警告: Fuyutsui_CountBars 空间不足!")
    if not startIndex then
        return nil
    end

    CreateHorizontalBarBackgrounds(startIndex, maxValue)

    local bar = CreateFrame("StatusBar", nil, countBars)
    bar:SetSize(maxValue * BAR_CONFIG.width + 1, BAR_CONFIG.height)
    bar:SetPoint("TOPLEFT", countBars, "TOPLEFT", (startIndex - 1) * BAR_CONFIG.width, 0)
    StyleHorizontalStatusBar(bar)
    bar:SetFrameLevel(BAR_STATUS_LEVEL)

    local function Refresh()
        local val = 0
        if valueType == "castCount" then
            val = C_Spell.GetSpellCastCount(spellId) or 0
        elseif valueType == "charge" then
            local charges = C_Spell.GetSpellCharges(spellId)
            if charges and Fuyutsui:IsSpellKnown(spellId) then
                val = charges.currentCharges or 0
            end
        end
        bar:SetMinMaxValues(minValue, maxValue)
        bar:SetValue(val)
    end

    for _, event in ipairs(BAR_EVENTS) do
        bar:RegisterEvent(event)
    end
    bar:SetScript("OnEvent", Refresh)
    Refresh()

    tinsert(createdBars, bar)
    spellIdToBar[spellId] = bar
    countBarLayoutEndIndex = nextAvailableIndex
    return bar
end

local function RefreshAllCreatedBars()
    for _, bar in ipairs(createdBars) do
        local onEvent = bar:GetScript("OnEvent")
        if onEvent then
            onEvent(bar, "PLAYER_ENTERING_WORLD")
        end
    end
end

function Fuyutsui:ClearAllFuyutsuiBars()
    for _, bar in ipairs(createdBars) do
        bar:UnregisterAllEvents()
        bar:SetScript("OnEvent", nil)
        bar:Hide()
        bar:SetParent(nil)
    end

    local regions = { countBars:GetRegions() }
    for _, region in ipairs(regions) do
        if region:IsObjectType("Texture") then
            ---@diagnostic disable-next-line: undefined-field
            region:SetColorTexture(0, 0, 0, 0)
            region:Hide()
        end
    end

    wipe(createdBars)
    wipe(spellIdToBar)
    nextAvailableIndex = BAR_START_INDEX
    countBarLayoutEndIndex = BAR_START_INDEX
    anyHorizontalBarLaidOut = false
    auraBarLaidOut = false
    if Fuyutsui.ReleasePlayerAuraContainers then
        Fuyutsui:ReleasePlayerAuraContainers()
    end
    if Fuyutsui.ReleaseGroupAuraContainers then
        Fuyutsui:ReleaseGroupAuraContainers()
    end
    if Fuyutsui.ReleaseOtherPriestAuraContainers then
        Fuyutsui:ReleaseOtherPriestAuraContainers()
    end
    if Fuyutsui.ClearGroupHealAbsorbBars then
        Fuyutsui:ClearGroupHealAbsorbBars()
    end
end

--[[============================================================================
    队伍治疗吸收条（FuyutsuiHealAbsorbBars）
    布局：主色块 + 计数条下方；每行 5 条、最多 30 条
    单槽：前锚点 1 + 条身 100 + 终点色块 1（列宽 102）
    编码：
      行 r：第 1 行=0 … 第 6 行=5（同行条身背景 r 统一）
      前锚点：(r=行号/255, g=单位编号/255, b=0)
        player=1, party1..4=2..5, raidN=N
      条身背景：(r=行号/255, g=相对索引1..100/255, b=单位编号/255)
      终点色块：BAR_END_COLOR（与 CountBars 相同）
    秘密值直通：UnitGetDetailedHealPrediction → GetHealAbsorbs → SetValue
============================================================================]]

local healAbsorbBars = CreateFrame("Frame", "FuyutsuiHealAbsorbBars", UIParent)
healAbsorbBars:SetSize(screenWidth, HEAL_ABSORB_ROWS * BAR_CONFIG.height)
healAbsorbBars:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, -(BLOCK_HEIGHT + BAR_HEIGHT))
healAbsorbBars:SetFrameStrata(BAR_STRATA)
healAbsorbBars:SetFrameLevel(BAR_LEVEL)

local healAbsorbSlots = {}      -- [slot] = { frame, bar, anchor, bodyTex, endTex, calculator, row, unit }
local healAbsorbUnitToSlot = {} -- [unit] = slot

--- player=1, party1..4=2..5, raidN=N
local function GetHealAbsorbUnitValue(unit)
    if unit == "player" then
        return 1
    end
    local partyIndex = string.match(unit, "^party(%d+)$")
    if partyIndex then
        return tonumber(partyIndex) + 1
    end
    local raidIndex = string.match(unit, "^raid(%d+)$")
    if raidIndex then
        return tonumber(raidIndex)
    end
    return 0
end

local function PaintHealAbsorbSlotColors(entry, unitValue)
    local rowR = entry.row / 255
    local unitB = unitValue / 255
    -- 前锚点：(r=行号, g=单位编号, b=0)
    entry.anchor:SetColorTexture(rowR, unitValue / 255, 0, 1)
    -- 条身：(r=行号, g=相对索引, b=单位编号)
    for i, tex in ipairs(entry.bodyTex) do
        tex:SetColorTexture(rowR, i / 255, unitB, 1)
    end
end

local function CreateHealAbsorbSlot(slot)
    local row = math.floor((slot - 1) / HEAL_ABSORB_COLS)
    local col = (slot - 1) % HEAL_ABSORB_COLS
    local originX = col * HEAL_ABSORB_SLOT_UNITS * HEAL_ABSORB_UNIT_WIDTH
    local originY = -row * BAR_CONFIG.height
    local rowR = row / 255

    local slotFrame = CreateFrame("Frame", nil, healAbsorbBars)
    slotFrame:SetSize(HEAL_ABSORB_SLOT_UNITS * HEAL_ABSORB_UNIT_WIDTH, BAR_CONFIG.height)
    slotFrame:SetPoint("TOPLEFT", healAbsorbBars, "TOPLEFT", originX, originY)
    slotFrame:Hide()

    -- 条前锚点：r=行号，g=单位编号（绑定时写入），b=0
    local anchor = slotFrame:CreateTexture(nil, "BACKGROUND")
    anchor:SetSize(HEAL_ABSORB_UNIT_WIDTH, BAR_CONFIG.height)
    anchor:SetPoint("TOPLEFT", slotFrame, "TOPLEFT", 0, 0)
    anchor:SetColorTexture(rowR, 0, 0, 1)

    -- 条身背景：r=行号，g=相对索引 1..100，b=单位编号（绑定时写入）
    local bodyTex = {}
    for i = 1, HEAL_ABSORB_BAR_UNITS do
        local tex = slotFrame:CreateTexture(nil, "BACKGROUND")
        tex:SetSize(HEAL_ABSORB_UNIT_WIDTH, BAR_CONFIG.height)
        tex:SetPoint("TOPLEFT", slotFrame, "TOPLEFT", i * HEAL_ABSORB_UNIT_WIDTH, 0)
        tex:SetColorTexture(rowR, i / 255, 0, 1)
        bodyTex[i] = tex
    end

    -- 条右侧终点色块（与 CountBars BAR_END_COLOR 相同）
    local endTex = slotFrame:CreateTexture(nil, "BACKGROUND")
    endTex:SetSize(HEAL_ABSORB_UNIT_WIDTH, BAR_CONFIG.height)
    endTex:SetPoint("TOPLEFT", slotFrame, "TOPLEFT", (1 + HEAL_ABSORB_BAR_UNITS) * HEAL_ABSORB_UNIT_WIDTH, 0)
    endTex:SetColorTexture(BAR_END_COLOR[1], BAR_END_COLOR[2], BAR_END_COLOR[3], BAR_END_COLOR[4])

    local bar = CreateFrame("StatusBar", nil, slotFrame)
    bar:SetSize(HEAL_ABSORB_BAR_UNITS * HEAL_ABSORB_UNIT_WIDTH + 1, BAR_CONFIG.height)
    bar:SetPoint("TOPLEFT", slotFrame, "TOPLEFT", HEAL_ABSORB_UNIT_WIDTH, 0)
    StyleHorizontalStatusBar(bar)
    bar:SetFrameLevel(BAR_STATUS_LEVEL)
    bar:SetMinMaxValues(0, 1)
    bar:SetValue(0)

    return {
        frame = slotFrame,
        bar = bar,
        anchor = anchor,
        bodyTex = bodyTex,
        endTex = endTex,
        calculator = CreateUnitHealPredictionCalculator(),
        row = row,
        unit = nil,
    }
end

for slot = 1, HEAL_ABSORB_MAX_SLOTS do
    healAbsorbSlots[slot] = CreateHealAbsorbSlot(slot)
end

function Fuyutsui:UpdateGroupHealAbsorbBar(unit)
    local slot = healAbsorbUnitToSlot[unit]
    if not slot then
        return
    end
    local entry = healAbsorbSlots[slot]
    if not entry or not entry.unit then
        return
    end
    if not UnitExists(unit) then
        entry.bar:SetMinMaxValues(0, 1)
        entry.bar:SetValue(0)
        return
    end
    UnitGetDetailedHealPrediction(unit, nil, entry.calculator)
    local amount = entry.calculator:GetHealAbsorbs()
    entry.bar:SetMinMaxValues(0, entry.calculator:GetMaximumHealth())
    entry.bar:SetValue(amount)
end

function Fuyutsui:ClearGroupHealAbsorbBars()
    wipe(healAbsorbUnitToSlot)
    for slot = 1, HEAL_ABSORB_MAX_SLOTS do
        local entry = healAbsorbSlots[slot]
        entry.unit = nil
        PaintHealAbsorbSlotColors(entry, 0)
        entry.bar:SetMinMaxValues(0, 1)
        entry.bar:SetValue(0)
        entry.frame:Hide()
    end
end

function Fuyutsui:RefreshGroupHealAbsorbBars()
    local groups = self.blocks and self.blocks.groups
    if not groups or not groups.healthPercent then
        self:ClearGroupHealAbsorbBars()
        return
    end

    wipe(healAbsorbUnitToSlot)
    local groupList = self.groupList or {}
    local bound = math.min(#groupList, HEAL_ABSORB_MAX_SLOTS)

    for slot = 1, HEAL_ABSORB_MAX_SLOTS do
        local entry = healAbsorbSlots[slot]
        if slot <= bound then
            local unit = groupList[slot]
            entry.unit = unit
            healAbsorbUnitToSlot[unit] = slot
            PaintHealAbsorbSlotColors(entry, GetHealAbsorbUnitValue(unit))
            entry.frame:Show()
            self:UpdateGroupHealAbsorbBar(unit)
        else
            entry.unit = nil
            PaintHealAbsorbSlotColors(entry, 0)
            entry.bar:SetMinMaxValues(0, 1)
            entry.bar:SetValue(0)
            entry.frame:Hide()
        end
    end
end

healAbsorbBars:RegisterEvent("UNIT_HEALTH")
healAbsorbBars:RegisterEvent("UNIT_MAXHEALTH")
healAbsorbBars:RegisterEvent("UNIT_HEAL_PREDICTION")
healAbsorbBars:RegisterEvent("UNIT_HEAL_ABSORB_AMOUNT_CHANGED")
healAbsorbBars:SetScript("OnEvent", function(_, _, unit)
    if unit and healAbsorbUnitToSlot[unit] then
        Fuyutsui:UpdateGroupHealAbsorbBar(unit)
    end
end)

--[[============================================================================
    AuraContainer（列表来自 ClassBlocks auras + spellId/spellIds）
    单位：player / target / focus；filter：HELPFUL / HARMFUL
    includeSpellIDs 可绑多个 ID：任一存在即显示（AuraSlot 取排序最前的一个）
    参考：AuraContainer_AI_Reference_zh-CN.md（PTR 7）
============================================================================]]

--- 归一化为 includeSpellIDs 集合；支持 spellId、spellIds=number 或 spellIds={ id1, id2 }
local function BuildIncludeSpellIDs(info)
    local set = {}
    if type(info.spellIds) == "table" then
        for _, id in ipairs(info.spellIds) do
            if type(id) == "number" then
                set[id] = true
            end
        end
    elseif type(info.spellIds) == "number" then
        set[info.spellIds] = true
    end
    if type(info.spellId) == "number" then
        set[info.spellId] = true
    end
    return set
end

local function CollectAuraSpellSlots(unitFilter)
    local slots = {}
    local auras = Fuyutsui.blocks and Fuyutsui.blocks.auras
    if not auras then
        return slots
    end
    for index, info in pairs(auras) do
        if type(info) == "table" then
            local unit = info.unit or "player"
            if not unitFilter or unit == unitFilter then
                local includeSpellIDs = BuildIncludeSpellIDs(info)
                if next(includeSpellIDs) then
                    tinsert(slots, {
                        index = index,
                        includeSpellIDs = includeSpellIDs,
                        maxApps = info.maxApps,
                        name = info.name,
                        unit = unit,
                        filter = info.filter or "HELPFUL",
                    })
                end
            end
        end
    end
    table.sort(slots, function(a, b)
        return a.index < b.index
    end)
    return slots
end

-- maxDuration 非 nil 时会排除永久光环（持续时间为 0）；取足够大的上限以覆盖常规限时光环
local AURA_TIMED_MAX_DURATION = 365 * 24 * 60 * 60
-- 非身份类过滤：maxDuration=0 不匹配任何光环（用于敌对单位上禁用 HELPFUL 等非法身份过滤场景）
local AURA_MATCH_NONE_FILTERS = { maxDuration = 0 }

local function AuraSlotFilters(includeSpellIDs, maxDuration)
    -- 每次新建集合，避免引擎改写原表后 spellId 过滤静默失效
    local spellIds
    if type(includeSpellIDs) == "table" then
        spellIds = {}
        for id, enabled in pairs(includeSpellIDs) do
            if enabled then
                spellIds[id] = true
            end
        end
    end
    local filters = {
        includeSpellIDs = spellIds,
    }
    if maxDuration ~= nil then
        filters.maxDuration = maxDuration
    end
    return filters
end

--- 敌对单位上 HELPFUL / 友方单位上 HARMFUL 时 includeSpellIDs 会被忽略，槽位可能误匹配任意光环。
--- 仅在反应允许的场景启用对应 filter。
local function IsAuraFilterAllowedForUnit(unit, filter)
    if not UnitExists(unit) then
        return false
    end
    if filter == "HELPFUL" or filter == "HELPFUL|PLAYER" then
        return UnitCanAssist("player", unit) and true or false
    end
    if filter == "HARMFUL" or filter == "HARMFUL|PLAYER" then
        return UnitCanAttack("player", unit) and not UnitCanAssist("player", unit)
    end
    return true
end

local function AuraBlockXOffset(index)
    return (index - 1) * BLOCK_FIX_CONFIG.blockWidth
end

local function ConfigureAuraButtonMouse(button)
    button:SetMouseMotionEnabled(AURA_ENABLE_MOUSE)
    if AURA_ENABLE_MOUSE then
        button:SetHideTooltipInCombat(true)
    end
end

local function AnchorAuraPixelButton(button, index)
    button:SetSize(AURA_BLOCK_W, AURA_BLOCK_H)
    button:SetClipsChildren(true)
    ConfigureAuraButtonMouse(button)
    button:SetPoint("TOPLEFT", UIParent, "TOPLEFT", AuraBlockXOffset(index), 0)
end

--- 对齐 CreateTexture(i, b)：绿通道编码索引，蓝通道将不足 1 秒钳为 1，其余编码到 255
local function MakeDurationColorCurve(index)
    local curve = C_CurveUtil.CreateColorCurve()
    curve:SetType(Enum.LuaCurveType.Linear)
    local r, g = EncodeBlockChannels(index)
    curve:AddPoint(0, CreateColor(r, g, 1 / 255, 1))
    curve:AddPoint(1, CreateColor(r, g, 1 / 255, 1))
    curve:AddPoint(255, CreateColor(r, g, 1, 1))
    return curve
end

--- 永久光环槽：整格底层 b=1（无 DurationText）
local function SetupPermanentAuraPixel(button, index)
    AnchorAuraPixelButton(button, index)
    local bg = button:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints(button)
    local r, g = EncodeBlockChannels(index)
    bg:SetColorTexture(r, g, 1, 1)
end

--- 限时光环槽：底层 b=0，█ 用剩余时间曲线；叠在永久槽之上
local function SetupTimedAuraDuration(button, index)
    AnchorAuraPixelButton(button, index)
    button:SetFrameLevel((button:GetFrameLevel() or 0) + 2)

    local bg = button:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints(button)
    local r, g = EncodeBlockChannels(index)
    bg:SetColorTexture(r, g, 0, 1)

    local duration = button:CreateFontString(nil, "ARTWORK", "GameFontNormal")
    duration:SetPoint("CENTER", button, "CENTER", 0, 0)
    duration:SetJustifyH("CENTER")
    duration:SetJustifyV("MIDDLE")
    button:SetDurationText(duration, {
        textFormat = {
            formatString = AURA_DURATION_CHAR,
            components = {},
        },
        textColor = {
            curve = MakeDurationColorCurve(index),
            property = Enum.DurationTextBindingProperty.RemainingDuration,
        },
    })
end

local function MakePermanentSlotInitializer(index)
    return function(button)
        SetupPermanentAuraPixel(button, index)
    end
end

local function MakeTimedSlotInitializer(index)
    return function(button)
        SetupTimedAuraDuration(button, index)
    end
end

local function AddDurationAuraSlotPair(container, slotKeyPrefix, filter, includeSpellIDs, index)
    -- 先限时后永久：若容器对 auraInstance 互斥分配，避免限时光环被永久槽抢走。
    -- 限时槽：maxDuration 排除永久；底层 b=0 + █ 曲线
    container:AddAuraSlot(slotKeyPrefix .. "_timed_" .. index, filter, {
        candidateFilters = AuraSlotFilters(includeSpellIDs, AURA_TIMED_MAX_DURATION),
        sortMethod = AuraContainerSortMethod.Expiration,
        sortDirection = AuraContainerSortDirection.Normal,
        initializeFrame = MakeTimedSlotInitializer(index),
    })
    -- 永久槽：无 maxDuration；永久命中时整格 b=1（限时若也命中则被上层限时槽盖住）
    container:AddAuraSlot(slotKeyPrefix .. "_permanent_" .. index, filter, {
        candidateFilters = AuraSlotFilters(includeSpellIDs),
        sortMethod = AuraContainerSortMethod.Expiration,
        sortDirection = AuraContainerSortDirection.Normal,
        initializeFrame = MakePermanentSlotInitializer(index),
    })

    container.fuyutsuiAuraSlots = container.fuyutsuiAuraSlots or {}
    tinsert(container.fuyutsuiAuraSlots, {
        keyPrefix = slotKeyPrefix,
        index = index,
        filter = filter,
        includeSpellIDs = includeSpellIDs,
    })
end

local function ApplyUnitAuraReactionFilters(container, unit)
    local slots = container and container.fuyutsuiAuraSlots
    if not slots then
        return
    end
    for _, slot in ipairs(slots) do
        local timedKey = slot.keyPrefix .. "_timed_" .. slot.index
        local permanentKey = slot.keyPrefix .. "_permanent_" .. slot.index
        if IsAuraFilterAllowedForUnit(unit, slot.filter) then
            container:SetAuraSlotCandidateFilters(
                timedKey,
                AuraSlotFilters(slot.includeSpellIDs, AURA_TIMED_MAX_DURATION)
            )
            container:SetAuraSlotCandidateFilters(
                permanentKey,
                AuraSlotFilters(slot.includeSpellIDs)
            )
        else
            -- 不用空 includeSpellIDs：非法身份场景下 SpellID 过滤会被忽略
            container:SetAuraSlotCandidateFilters(timedKey, AURA_MATCH_NONE_FILTERS)
            container:SetAuraSlotCandidateFilters(permanentKey, AURA_MATCH_NONE_FILTERS)
        end
    end
end

local function KickAuraContainer(container, unit)
    if not container then
        return
    end
    if unit then
        container.fuyutsuiUnit = unit
        container:SetUnit(unit)
    end
end

--- 重绑全部光环槽的 spellId 候选过滤，并强制容器重新分配光环
--- 顺序：SetUnit → SetAuraSlotCandidateFilters → UpdateAllAuras（避免 SetUnit 冲掉过滤）
local function RebindContainerSpellFilters(container, unit)
    if not container then
        return
    end
    local bindUnit = unit or container.fuyutsuiUnit
    KickAuraContainer(container, bindUnit)

    if container.fuyutsuiAuraSlots then
        ApplyUnitAuraReactionFilters(container, bindUnit or "player")
    end
    if container.fuyutsuiBarSlots then
        table.sort(container.fuyutsuiBarSlots, function(a, b)
            return (a.index or 0) < (b.index or 0)
        end)
        for _, slot in ipairs(container.fuyutsuiBarSlots) do
            if slot.filter and not IsAuraFilterAllowedForUnit(bindUnit or "player", slot.filter) then
                container:SetAuraSlotCandidateFilters(slot.key, AURA_MATCH_NONE_FILTERS)
            else
                container:SetAuraSlotCandidateFilters(slot.key, AuraSlotFilters(slot.includeSpellIDs))
            end
        end
    end
    if container.fuyutsuiSpellIdSlots then
        for _, slot in ipairs(container.fuyutsuiSpellIdSlots) do
            container:SetAuraSlotCandidateFilters(slot.key, AuraSlotFilters(slot.includeSpellIDs))
        end
    end
    if container.fuyutsuiDispelSlot then
        local dispel = container.fuyutsuiDispelSlot
        container:SetAuraSlotCandidateFilters(dispel.key, {
            includeDispelTypes = dispel.includeDispelTypes,
        })
    end
    if container.fuyutsuiUnitDispelSlots then
        local canAttack = bindUnit and UnitCanAttack("player", bindUnit)
        local canAssist = bindUnit and UnitCanAssist("player", bindUnit)
        for _, slot in ipairs(container.fuyutsuiUnitDispelSlots) do
            local enabled = (slot.reaction == "enemy" and canAttack and not canAssist)
                or (slot.reaction == "friend" and canAssist)
            local source = slot.reaction == "enemy"
                and Fuyutsui.includeOffensiveDispelTypes
                or Fuyutsui.includeDispelTypes
            local includeDispelTypes = {}
            if enabled and type(source) == "table" then
                for name, canDispel in pairs(source) do
                    if canDispel then
                        includeDispelTypes[name] = true
                    end
                end
            end
            if next(includeDispelTypes) then
                container:SetAuraSlotCandidateFilters(slot.key, {
                    includeDispelTypes = includeDispelTypes,
                })
            else
                container:SetAuraSlotCandidateFilters(slot.key, AURA_MATCH_NONE_FILTERS)
            end
        end
    end

    if container.UpdateAllAuras then
        container:UpdateAllAuras()
    end
end

-- 驱散类型 -> 蓝通道编码（防御驱散与进攻驱散共用）
local DISPEL_TYPE_COLOR_IDS = {
    Magic = 1,
    Curse = 2,
    Disease = 3,
    Poison = 4,
    Enrage = 9,
    Bleed = 11,
}

--- 驱散像素：固定纹理 + 按驱散类型写死颜色（非剩余时间）
local function MakeDispelColorMap(index)
    local r, g = EncodeBlockChannels(index)
    local map = {}
    for name, id in pairs(DISPEL_TYPE_COLOR_IDS) do
        map[name] = CreateColor(r, g, id / 255, 1)
    end
    return map
end

local function SetupDispelTypePixel(button, index, showWhenHarmful, showWhenHelpful)
    button:SetSize(AURA_BLOCK_W, AURA_BLOCK_H)
    button:SetClipsChildren(true)
    ConfigureAuraButtonMouse(button)
    button:SetPoint("TOPLEFT", UIParent, "TOPLEFT", AuraBlockXOffset(index), 0)

    local tex = button:CreateTexture(nil, "ARTWORK")
    tex:SetAllPoints(button)
    tex:SetTexture("Interface\\ChatFrame\\ChatFrameBackground")
    tex:SetVertexColor(1, 1, 1, 1)

    button:AddDispelTypeTexture(tex, {
        showWhenHarmful = showWhenHarmful ~= false,
        showWhenHelpful = showWhenHelpful == true,
        showWithoutDispelType = false,
        style = Enum.CustomAuraButtonDispelTypeTextureStyle.PreserveAsset,
        customDispelColorMap = MakeDispelColorMap(index),
    })
end

local function MakeDispelSlotInitializer(index, showWhenHarmful, showWhenHelpful)
    return function(button)
        SetupDispelTypePixel(button, index, showWhenHarmful, showWhenHelpful)
    end
end

--- 层数条：与计数条同一套坐标/背景编码，StatusBar 由 AuraContainer 驱动
local function AnchorApplicationBarButton(button, maxApps, startIndex)
    button:SetSize(maxApps * BAR_CONFIG.width, BAR_CONFIG.height)
    ConfigureAuraButtonMouse(button)
    -- 右移 1px，避免白色填充未完全盖住背后背景色
    button:ClearAllPoints()
    button:SetPoint("TOPLEFT", countBars, "TOPLEFT", (startIndex - 1) * BAR_CONFIG.width + 1, 0)
end

local function SetupApplicationBarOnly(button, maxApps, startIndex)
    AnchorApplicationBarButton(button, maxApps, startIndex)

    local bar = CreateFrame("StatusBar", nil, button)
    bar:SetAllPoints(button)
    StyleHorizontalStatusBar(bar)
    bar:SetFrameLevel((button:GetFrameLevel() or 0) + 1)

    button:SetApplicationBar(bar, {
        maxApplications = maxApps,
    })
end

local function MakeBarSlotInitializer(slotInfo)
    return function(button)
        SetupApplicationBarOnly(button, slotInfo.maxApps, slotInfo.startIndex)
        slotInfo.button = button
    end
end

local AURA_BAR_UNIT_ORDER = { "player", "target", "focus" }

local function CollectAuraApplicationSlots(unit)
    local appSlots = {}
    for _, info in ipairs(CollectAuraSpellSlots(unit)) do
        -- 玩家光环保留原有层数条；目标/焦点只为玩家施放的有害光环显示层数。
        local isTargetDebuff = unit ~= "player"
            and (info.filter == "HARMFUL|PLAYER" or info.filter == "HARMFUL")
        if info.maxApps and (unit == "player" or isTargetDebuff) then
            tinsert(appSlots, info)
        end
    end
    return appSlots
end

local function ReclaimAuraBarLayoutSpace()
    nextAvailableIndex = countBarLayoutEndIndex
    anyHorizontalBarLaidOut = countBarLayoutEndIndex > BAR_START_INDEX
    auraBarLaidOut = false
    UpdateHorizontalBarEndMarker()
end

local function AuraBarSlotsMatch(container, appSlots)
    local slots = container and container.fuyutsuiBarSlots
    if not slots or #slots ~= #appSlots then
        return false
    end
    for i, info in ipairs(appSlots) do
        local slot = slots[i]
        if not slot
            or slot.index ~= info.index
            or slot.maxApps ~= info.maxApps
            or slot.filter ~= info.filter then
            return false
        end
    end
    return true
end

local function ReleaseFrame(frame)
    if not frame then
        return
    end
    frame:SetEnabled(false)
    frame:Hide()
    frame:SetParent(nil)
end

local UNIT_AURA_CONTAINER_KEYS = {
    player = "PlayerAuraContainer",
    target = "TargetAuraContainer",
    focus = "FocusAuraContainer",
}

local UNIT_AURA_BAR_CONTAINER_KEYS = {
    player = "PlayerAuraBarContainer",
    target = "TargetAuraBarContainer",
    focus = "FocusAuraBarContainer",
}

function Fuyutsui:ReleaseUnitAuraContainers()
    for _, key in pairs(UNIT_AURA_CONTAINER_KEYS) do
        ReleaseFrame(Fuyutsui[key])
        Fuyutsui[key] = nil
    end
    for _, key in pairs(UNIT_AURA_BAR_CONTAINER_KEYS) do
        ReleaseFrame(Fuyutsui[key])
        Fuyutsui[key] = nil
    end
    ReclaimAuraBarLayoutSpace()
end

-- 兼容旧名
function Fuyutsui:ReleasePlayerAuraContainers()
    self:ReleaseUnitAuraContainers()
end

local function GetUnitDispelStateIndex(unit)
    local stateBlocks = Fuyutsui.blocks and Fuyutsui.blocks.state
    if not stateBlocks then
        return nil
    end
    if unit == "target" then
        return stateBlocks["目标驱散类型"]
    elseif unit == "focus" then
        return stateBlocks["焦点驱散类型"]
    end
end

local function AddUnitDispelSlots(container, unit, index)
    if not index then
        return
    end

    local slots = {
        {
            key = unit .. "_dispel_helpful",
            filter = "HELPFUL",
            reaction = "enemy",
            initializeFrame = MakeDispelSlotInitializer(index, false, true),
        },
        {
            key = unit .. "_dispel_harmful",
            filter = "HARMFUL",
            reaction = "friend",
            initializeFrame = MakeDispelSlotInitializer(index, true, false),
        },
    }
    for _, slot in ipairs(slots) do
        container:AddAuraSlot(slot.key, slot.filter, {
            candidateFilters = AURA_MATCH_NONE_FILTERS,
            sortMethod = AuraContainerSortMethod.Expiration,
            sortDirection = AuraContainerSortDirection.Normal,
            initializeFrame = slot.initializeFrame,
        })
    end
    container.fuyutsuiUnitDispelSlots = slots
end

local function CreateUnitAuraDurationSlots(unit, spellSlots, dispelIndex)
    if (not spellSlots or #spellSlots == 0) and not dispelIndex then
        return
    end

    local key = UNIT_AURA_CONTAINER_KEYS[unit]
    if not key then
        return
    end

    EnsureAuraContainerLoaded()

    local frameName = "Fuyutsui" .. unit:gsub("^%l", string.upper) .. "AuraDurationSlots"
    local durationSlots = CreateFrame("AuraContainer", frameName, UIParent, "CustomAuraContainerTemplate")
    durationSlots:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    durationSlots:SetUnit(unit)
    durationSlots:SetEnabled(true)
    durationSlots:SetFrameStrata(AURA_DURATION_STRATA)
    durationSlots:SetFrameLevel(AURA_DURATION_LEVEL)

    for _, info in ipairs(spellSlots) do
        local filter = info.filter or "HELPFUL"
        AddDurationAuraSlotPair(durationSlots, "duration_index", filter, info.includeSpellIDs, info.index)
    end

    AddUnitDispelSlots(durationSlots, unit, dispelIndex)

    Fuyutsui[key] = durationSlots
    durationSlots.fuyutsuiUnit = unit
    ApplyUnitAuraReactionFilters(durationSlots, unit)
    RebindContainerSpellFilters(durationSlots, unit)
end

function Fuyutsui:RefreshUnitAuraContainers()
    for unit, key in pairs(UNIT_AURA_CONTAINER_KEYS) do
        if not Fuyutsui[key] then
            local spellSlots = CollectAuraSpellSlots(unit)
            local dispelIndex = GetUnitDispelStateIndex(unit)
            if #spellSlots > 0 or dispelIndex then
                CreateUnitAuraDurationSlots(unit, spellSlots, dispelIndex)
            end
        end
    end
end

--- 切换 target/focus 时：按敌友启用对应 filter，并整表刷新
function Fuyutsui:UpdateUnitAuraContainer(unit)
    local key = UNIT_AURA_CONTAINER_KEYS[unit]
    if not key then
        return
    end
    RebindContainerSpellFilters(Fuyutsui[key], unit)
    RebindContainerSpellFilters(Fuyutsui[UNIT_AURA_BAR_CONTAINER_KEYS[unit]], unit)
end

-- 兼容旧名
function Fuyutsui:RefreshPlayerAuraContainers()
    self:RefreshUnitAuraContainers()
end

--- 在计数条之后排布层数条，最后放置 BAR_END_COLOR
--- 按 auras 索引升序固定条序；若容器已存在但槽位集合变化则整表重建
function Fuyutsui:LayoutAuraApplicationBars()
    local slotsByUnit = {}
    local needsRebuild = not auraBarLaidOut
    for _, unit in ipairs(AURA_BAR_UNIT_ORDER) do
        local appSlots = CollectAuraApplicationSlots(unit)
        slotsByUnit[unit] = appSlots
        local key = UNIT_AURA_BAR_CONTAINER_KEYS[unit]
        local hasContainer = Fuyutsui[key] ~= nil
        if not AuraBarSlotsMatch(Fuyutsui[key], appSlots) then
            -- 无配置的单位也应视为匹配，避免每帧重建空容器。
            if #appSlots > 0 or hasContainer then
                needsRebuild = true
            end
        end
    end

    if not needsRebuild then
        return
    end

    for _, key in pairs(UNIT_AURA_BAR_CONTAINER_KEYS) do
        ReleaseFrame(Fuyutsui[key])
        Fuyutsui[key] = nil
    end

    -- 层数条必须紧接计数条之后，按 player → target → focus 排布。
    nextAvailableIndex = countBarLayoutEndIndex
    anyHorizontalBarLaidOut = countBarLayoutEndIndex > BAR_START_INDEX

    for _, unit in ipairs(AURA_BAR_UNIT_ORDER) do
        local appSlots = slotsByUnit[unit]
        if #appSlots > 0 then
        EnsureAuraContainerLoaded()

            local key = UNIT_AURA_BAR_CONTAINER_KEYS[unit]
            local frameName = "Fuyutsui" .. unit:gsub("^%l", string.upper) .. "AuraBarSlots"
            local barSlots = CreateFrame("AuraContainer", frameName, countBars,
                                         "CustomAuraContainerTemplate")
            barSlots:SetPoint("TOPLEFT", countBars, "TOPLEFT", 0, 0)
            barSlots:SetUnit(unit)
            barSlots:SetEnabled(true)
            barSlots:SetFrameStrata(AURA_BAR_STRATA)
            barSlots:SetFrameLevel(AURA_BAR_LEVEL)
            barSlots.fuyutsuiBarSlots = {}
            barSlots.fuyutsuiUnit = unit

            for _, info in ipairs(appSlots) do
                local startIndex = ReserveHorizontalBarUnits(
                    info.maxApps,
                    "警告: Fuyutsui_CountBars 光环层数条空间不足!"
                )
                if not startIndex then
                    break
                end
                CreateHorizontalBarBackgrounds(startIndex, info.maxApps)
                local slotKey = unit .. "_bar_index_" .. info.index
                local slotInfo = {
                    key = slotKey,
                    index = info.index,
                    maxApps = info.maxApps,
                    startIndex = startIndex,
                    includeSpellIDs = info.includeSpellIDs,
                    filter = info.filter,
                }
                barSlots:AddAuraSlot(slotKey, info.filter or "HELPFUL", {
                    candidateFilters = AuraSlotFilters(info.includeSpellIDs),
                    sortMethod = AuraContainerSortMethod.Expiration,
                    sortDirection = AuraContainerSortDirection.Normal,
                    initializeFrame = MakeBarSlotInitializer(slotInfo),
                })
                tinsert(barSlots.fuyutsuiBarSlots, slotInfo)
            end
            Fuyutsui[key] = barSlots
            RebindContainerSpellFilters(barSlots, unit)
        end
    end

    UpdateHorizontalBarEndMarker()
    auraBarLaidOut = true
end

--[[============================================================================
    队伍成员 AuraContainer
    配置：
      groups.aura[offset] = { name, spellId/spellIds }  -- 默认 HELPFUL|PLAYER，剩余时间色块
      groups.dispel = offset                            -- HARMFUL，按可驱散类型过滤；固定纹理按类型着色
    像素：start + (memberIndex-1)*num + offset
    驱散蓝通道：Magic=1 Curse=2 Disease=3 Poison=4 Bleed=11（/255）
============================================================================]]

local groupAuraContainers = {} -- [memberIndex] = AuraContainer

local function CollectGroupAuraDefs(auraTable)
    local defs = {}
    if type(auraTable) ~= "table" then
        return defs
    end
    for offset, info in pairs(auraTable) do
        if type(offset) == "number" and type(info) == "table" then
            local includeSpellIDs = BuildIncludeSpellIDs(info)
            if next(includeSpellIDs) then
                tinsert(defs, {
                    offset = offset,
                    includeSpellIDs = includeSpellIDs,
                    name = info.name,
                })
            end
        end
    end
    table.sort(defs, function(a, b)
        return a.offset < b.offset
    end)
    return defs
end

local function GroupAuraPixelIndex(groups, memberIndex, offset)
    return groups.start + (memberIndex - 1) * groups.num + offset
end

local function CopyIncludeDispelTypes()
    local src = Fuyutsui.includeDispelTypes
    if type(src) ~= "table" then
        return nil
    end
    local dst = {}
    local any = false
    for name, enabled in pairs(src) do
        if enabled then
            dst[name] = true
            any = true
        end
    end
    if not any then
        return nil
    end
    return dst
end

function Fuyutsui:ReleaseGroupAuraContainers()
    for memberIndex, container in pairs(groupAuraContainers) do
        ReleaseFrame(container)
        groupAuraContainers[memberIndex] = nil
    end
end

local function CreateGroupMemberAuraContainer(memberIndex, groups, auraDefs, includeDispelTypes)
    EnsureAuraContainerLoaded()

    local container = CreateFrame("AuraContainer", "FuyutsuiGroupAuraSlots_" .. memberIndex, UIParent,
                                  "CustomAuraContainerTemplate")
    container:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    container:SetEnabled(true)
    container:SetFrameStrata(AURA_DURATION_STRATA)
    container:SetFrameLevel(AURA_DURATION_LEVEL)

    for _, def in ipairs(auraDefs) do
        local pixelIndex = GroupAuraPixelIndex(groups, memberIndex, def.offset)
        if pixelIndex > 0 and pixelIndex <= BLOCK_FIX_COUNT then
            -- 救赎之魂（27827）可能由其他牧师施加；该槽只按 HELPFUL 筛选，不能限制 PLAYER。
            local filter = def.includeSpellIDs[27827] and "HELPFUL" or "HELPFUL|PLAYER"
            AddDurationAuraSlotPair(
                container,
                "group_" .. memberIndex .. "_aura_" .. def.offset,
                filter,
                def.includeSpellIDs,
                pixelIndex
            )
        end
    end

    -- 可驱散减益：仅包含玩家当前会的驱散类型；像素显示类型固定色，非剩余时间
    if groups.dispel and includeDispelTypes then
        local pixelIndex = GroupAuraPixelIndex(groups, memberIndex, groups.dispel)
        if pixelIndex > 0 and pixelIndex <= BLOCK_FIX_COUNT then
            local dispelKey = "group_" .. memberIndex .. "_dispel"
            container:AddAuraSlot(
                dispelKey,
                "HARMFUL",
                {
                    candidateFilters = {
                        includeDispelTypes = includeDispelTypes,
                    },
                    sortMethod = AuraContainerSortMethod.Expiration,
                    sortDirection = AuraContainerSortDirection.Normal,
                    initializeFrame = MakeDispelSlotInitializer(pixelIndex),
                }
            )
            container.fuyutsuiDispelSlot = {
                key = dispelKey,
                includeDispelTypes = includeDispelTypes,
            }
        end
    end

    return container
end

--- 按当前 groupList 为每个成员创建/绑定单位光环槽
function Fuyutsui:RefreshGroupAuraContainers()
    local groups = Fuyutsui.blocks and Fuyutsui.blocks.groups
    if not groups or not groups.start or not groups.num then
        self:ReleaseGroupAuraContainers()
        return
    end

    local auraDefs = CollectGroupAuraDefs(groups.aura)
    local includeDispelTypes = groups.dispel and CopyIncludeDispelTypes() or nil
    if #auraDefs == 0 and not includeDispelTypes then
        self:ReleaseGroupAuraContainers()
        return
    end

    local groupList = Fuyutsui.groupList or {}
    local group = Fuyutsui.group or {}
    local used = {}

    for _, unit in ipairs(groupList) do
        local obj = group[unit]
        if obj and obj.index then
            local memberIndex = obj.index
            used[memberIndex] = true
            local container = groupAuraContainers[memberIndex]
            if not container then
                container = CreateGroupMemberAuraContainer(memberIndex, groups, auraDefs, includeDispelTypes)
                groupAuraContainers[memberIndex] = container
            end
            container.fuyutsuiUnit = unit
            container:SetUnit(unit)
            container:SetEnabled(true)
            container:Show()
        end
    end

    for memberIndex, container in pairs(groupAuraContainers) do
        if not used[memberIndex] then
            container:SetEnabled(false)
            container:Hide()
        end
    end
end

--[[============================================================================
    其他牧师光环槽（救赎之魂1/2）
    团队中除玩家外的前 2 名牧师；有 spell 194384 时色块 b=raid编号/255，否则隐藏（底层 0）
============================================================================]]

local OTHER_PRIEST_AURA_SPELL_ID = 194384
local OTHER_PRIEST_SLOT_NAMES = { "救赎之魂1", "救赎之魂2" }
local otherPriestAuraContainers = {} -- [slot] = AuraContainer

local function SetupRaidIndexAuraPixel(button, pixelIndex, raidIndex)
    AnchorAuraPixelButton(button, pixelIndex)
    local bg = button:CreateTexture(nil, "BACKGROUND")
    bg:SetAllPoints(button)
    local r, g = EncodeBlockChannels(pixelIndex)
    bg:SetColorTexture(r, g, raidIndex / 255, 1)
end

local function MakeRaidIndexSlotInitializer(pixelIndex, raidIndex)
    return function(button)
        SetupRaidIndexAuraPixel(button, pixelIndex, raidIndex)
    end
end

function Fuyutsui:ReleaseOtherPriestAuraContainers()
    for slot, container in pairs(otherPriestAuraContainers) do
        ReleaseFrame(container)
        otherPriestAuraContainers[slot] = nil
    end
end

function Fuyutsui:RefreshOtherPriestAuraContainers()
    self:ReleaseOtherPriestAuraContainers()

    local blocks = self.blocks
    local stateBlocks = blocks and blocks.state
    if not stateBlocks then
        return
    end

    local pixelIndices = {}
    for slot, name in ipairs(OTHER_PRIEST_SLOT_NAMES) do
        pixelIndices[slot] = stateBlocks[name]
    end
    if not pixelIndices[1] and not pixelIndices[2] then
        return
    end
    if not IsInRaid() then
        return
    end

    EnsureAuraContainerLoaded()

    local found = 0
    local num = GetNumGroupMembers()
    for raidIndex = 1, num do
        local unit = "raid" .. raidIndex
        if UnitExists(unit) and not UnitIsUnit(unit, "player") and UnitClassBase(unit) == "PRIEST" then
            found = found + 1
            local pixelIndex = pixelIndices[found]
            if pixelIndex then
                local includeSpellIDs = { [OTHER_PRIEST_AURA_SPELL_ID] = true }
                local container = CreateFrame(
                    "AuraContainer",
                    "FuyutsuiOtherPriestAura_" .. found,
                    UIParent,
                    "CustomAuraContainerTemplate"
                )
                container:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
                container:SetEnabled(true)
                container:SetFrameStrata(AURA_DURATION_STRATA)
                container:SetFrameLevel(AURA_DURATION_LEVEL)

                local slotKey = "other_priest_" .. found
                container:AddAuraSlot(slotKey, "HELPFUL", {
                    candidateFilters = AuraSlotFilters(includeSpellIDs),
                    sortMethod = AuraContainerSortMethod.Expiration,
                    sortDirection = AuraContainerSortDirection.Normal,
                    initializeFrame = MakeRaidIndexSlotInitializer(pixelIndex, raidIndex),
                })
                container.fuyutsuiSpellIdSlots = {
                    {
                        key = slotKey,
                        includeSpellIDs = includeSpellIDs,
                    },
                }
                container.fuyutsuiUnit = unit
                container:SetUnit(unit)
                otherPriestAuraContainers[found] = container
            end
            if found >= #OTHER_PRIEST_SLOT_NAMES then
                break
            end
        end
    end
end

--- 过场后重绑全部光环槽的 spellId / 驱散过滤，避免槽位落到“第一个光环”
--- 同时按配置重排/刷新全部横向条（计数条 + 层数条），保证条序不漂
function Fuyutsui:RebindAuraSpellFilters()
    for _, unit in ipairs(UNIT_AURA_REBIND_ORDER) do
        local key = UNIT_AURA_CONTAINER_KEYS[unit]
        RebindContainerSpellFilters(Fuyutsui[key], unit)
    end

    -- 层数条：按 auras 索引同步槽位；集合变化时整表重建，并重锚保证条序
    self:LayoutAuraApplicationBars()
    for _, unit in ipairs(AURA_BAR_UNIT_ORDER) do
        local key = UNIT_AURA_BAR_CONTAINER_KEYS[unit]
        local barContainer = Fuyutsui[key]
        if barContainer and barContainer.fuyutsuiBarSlots then
            local appSlots = CollectAuraApplicationSlots(unit)
            for i, slot in ipairs(barContainer.fuyutsuiBarSlots) do
                local info = appSlots[i]
                if info then
                    slot.includeSpellIDs = info.includeSpellIDs
                    slot.filter = info.filter
                end
            end
            RebindContainerSpellFilters(barContainer, unit)
            table.sort(barContainer.fuyutsuiBarSlots, function(a, b)
                return (a.index or 0) < (b.index or 0)
            end)
            for _, slot in ipairs(barContainer.fuyutsuiBarSlots) do
                if slot.button and slot.startIndex and slot.maxApps then
                    AnchorApplicationBarButton(slot.button, slot.maxApps, slot.startIndex)
                end
            end
        end
    end

    RefreshAllCreatedBars()

    local groupIndices = {}
    for memberIndex in pairs(groupAuraContainers) do
        tinsert(groupIndices, memberIndex)
    end
    table.sort(groupIndices)
    for _, memberIndex in ipairs(groupIndices) do
        local container = groupAuraContainers[memberIndex]
        RebindContainerSpellFilters(container, container.fuyutsuiUnit)
    end

    for slot = 1, #OTHER_PRIEST_SLOT_NAMES do
        local container = otherPriestAuraContainers[slot]
        if container then
            RebindContainerSpellFilters(container, container.fuyutsuiUnit)
        end
    end
end
