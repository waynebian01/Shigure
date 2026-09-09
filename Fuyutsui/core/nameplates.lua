local addon, ns = ...

-- 姓名板像素协议：一行标记 + 动态数据行，每行固定 20 个姓名板槽位。
-- 标记行第一个单元为 (1/255, 0, 1/255)，第二个单元 B 通道编码数据行数。
-- 数据单元 R=1/255 表示单位存在，B 通道编码 0..255 的原始值。
local NAMEPLATE_SLOT_COUNT = 20
local NAMEPLATE_MAX_ROWS = 255
local NAMEPLATE_CELL_HEIGHT = 1
local NAMEPLATE_MARKER_R = 1 / 255
local NAMEPLATE_MARKER_B = 1 / 255
local NAMEPLATE_FRAME_STRATA = "TOOLTIP"
local NAMEPLATE_FRAME_LEVEL = 9005

local screenWidth = GetScreenWidth()
local cellWidth = screenWidth / NAMEPLATE_SLOT_COUNT
local nameplateFrame = CreateFrame("Frame", "FuyutsuiNameplatePixels", UIParent)
nameplateFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, -(1 + 2 + 12))
nameplateFrame:SetWidth(screenWidth)
nameplateFrame:SetFrameStrata(NAMEPLATE_FRAME_STRATA)
nameplateFrame:SetFrameLevel(NAMEPLATE_FRAME_LEVEL)
nameplateFrame:Hide()

local rowTextures = {}
local rowCount = 0
local auraContainers = {}

local function EnsureRow(row)
    if rowTextures[row] then
        return rowTextures[row]
    end

    local textures = {}
    for slot = 1, NAMEPLATE_SLOT_COUNT do
        local tex = nameplateFrame:CreateTexture(nil, "BACKGROUND")
        tex:SetSize(cellWidth, NAMEPLATE_CELL_HEIGHT)
        tex:SetPoint("TOPLEFT", nameplateFrame, "TOPLEFT", (slot - 1) * cellWidth, -(row - 1) * NAMEPLATE_CELL_HEIGHT)
        tex:SetColorTexture(0, 0, 0, 1)
        textures[slot] = tex
    end
    rowTextures[row] = textures
    return textures
end

local function SetCell(row, slot, present, value)
    if row < 1 or row > NAMEPLATE_MAX_ROWS + 1 or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then
        return
    end
    local tex = EnsureRow(row)[slot]
    tex:SetColorTexture(present and NAMEPLATE_MARKER_R or 0, 0, math.max(0, math.min(255, value or 0)) / 255, 1)
end

-- Secret values returned by UnitHealthPercent():GetRGB() cannot participate in
-- Lua arithmetic or comparisons.  They are already normalized to 0..1, so
-- write the channel directly and let the protected API consume it.
local function SetNormalizedCell(row, slot, present, value)
    if row < 1 or row > NAMEPLATE_MAX_ROWS + 1 or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then
        return
    end
    local tex = EnsureRow(row)[slot]
    tex:SetColorTexture(present and NAMEPLATE_MARKER_R or 0, 0, value, 1)
end

local function ClearSlot(slot)
    for row = 2, rowCount + 1 do
        SetCell(row, slot, false, 0)
    end
end

local function GetNameplateTopOffset()
    -- block.lua 当前布局：主色块 1 行、计数条 2 行、治疗吸收网格 6×2 行。
    return -(1 + 2 + 12)
end

local function ReleaseAuraContainer(slot)
    local container = auraContainers[slot]
    if not container then
        return
    end
    container:SetEnabled(false)
    container:Hide()
    container:SetParent(nil)
    auraContainers[slot] = nil
end

local function NameplateAuraColorCurve()
    local curve = C_CurveUtil.CreateColorCurve()
    curve:SetType(Enum.LuaCurveType.Linear)
    curve:AddPoint(0, CreateColor(NAMEPLATE_MARKER_R, 0, 1 / 255, 1))
    curve:AddPoint(1, CreateColor(NAMEPLATE_MARKER_R, 0, 1 / 255, 1))
    curve:AddPoint(255, CreateColor(NAMEPLATE_MARKER_R, 0, 1, 1))
    return curve
end

local function CreateNameplateAuraContainer(slot, unit, config)
    if not config or type(config.auras) ~= "table" then
        return nil
    end
    local auras = config.auras
    if #auras == 0 then
        return nil
    end

    if C_AddOns and not C_AddOns.IsAddOnLoaded("Blizzard_AuraContainer") then
        C_AddOns.LoadAddOn("Blizzard_AuraContainer")
    end

    local container = CreateFrame(
        "AuraContainer",
        "FuyutsuiNameplateAuraSlots_" .. slot,
        UIParent,
        "CustomAuraContainerTemplate"
    )
    container:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    container:SetUnit(unit)
    container:SetEnabled(true)
    container:SetFrameStrata(NAMEPLATE_FRAME_STRATA)
    container:SetFrameLevel(NAMEPLATE_FRAME_LEVEL + 1)

    local baseRow = math.max(config.healthPercent or 0, config.range or 0) + 1
    for auraIndex, aura in ipairs(auras) do
        if type(aura) == "table" and (aura.spellId or aura.spellIds) then
            local includeSpellIDs = {}
            if aura.spellId then
                includeSpellIDs[aura.spellId] = true
            end
            if type(aura.spellIds) == "table" then
                for _, spellId in ipairs(aura.spellIds) do
                    includeSpellIDs[spellId] = true
                end
            end
            local row = baseRow + auraIndex - 1
            local keyPrefix = "nameplate_" .. slot .. "_aura_" .. auraIndex
            local function initializeTimed(button)
                button:SetFrameLevel((button:GetFrameLevel() or 0) + 2)
                button:SetSize(cellWidth, NAMEPLATE_CELL_HEIGHT)
                button:SetClipsChildren(true)
                button:SetPoint("TOPLEFT", nameplateFrame, "TOPLEFT", (slot - 1) * cellWidth, -(row) * NAMEPLATE_CELL_HEIGHT)
                local bg = button:CreateTexture(nil, "BACKGROUND")
                bg:SetAllPoints(button)
                bg:SetColorTexture(NAMEPLATE_MARKER_R, 0, 0, 1)
                local duration = button:CreateFontString(nil, "ARTWORK", "GameFontNormal")
                duration:SetPoint("CENTER", button, "CENTER", 0, 0)
                duration:SetTextColor(0, 0, 0, 0)
                button:SetDurationText(duration, {
                    textFormat = { formatString = "█", components = {} },
                    textColor = { curve = NameplateAuraColorCurve(), property = Enum.DurationTextBindingProperty.RemainingDuration },
                })
            end
            local function initializePermanent(button)
                button:SetFrameLevel((button:GetFrameLevel() or 0) + 1)
                button:SetSize(cellWidth, NAMEPLATE_CELL_HEIGHT)
                button:SetClipsChildren(true)
                button:SetPoint("TOPLEFT", nameplateFrame, "TOPLEFT", (slot - 1) * cellWidth, -(row) * NAMEPLATE_CELL_HEIGHT)
                local bg = button:CreateTexture(nil, "BACKGROUND")
                bg:SetAllPoints(button)
                bg:SetColorTexture(NAMEPLATE_MARKER_R, 0, 1, 1)
            end
            container:AddAuraSlot(keyPrefix .. "_timed", "HARMFUL|PLAYER", {
                candidateFilters = { includeSpellIDs = includeSpellIDs, maxDuration = 365 * 24 * 60 * 60 },
                sortMethod = AuraContainerSortMethod.Expiration,
                sortDirection = AuraContainerSortDirection.Normal,
                initializeFrame = initializeTimed,
            })
            container:AddAuraSlot(keyPrefix .. "_permanent", "HARMFUL|PLAYER", {
                candidateFilters = { includeSpellIDs = includeSpellIDs },
                sortMethod = AuraContainerSortMethod.Expiration,
                sortDirection = AuraContainerSortDirection.Normal,
                initializeFrame = initializePermanent,
            })
        end
    end
    container:Show()
    return container
end

local function RefreshAuraContainer(slot, unit, config)
    if not config or type(config.auras) ~= "table" or #config.auras == 0 then
        ReleaseAuraContainer(slot)
        return
    end
    local current = auraContainers[slot]
    if current and current.fuyutsuiUnit == unit then
        current:SetUnit(unit)
        current:SetEnabled(true)
        current:Show()
        return
    end
    ReleaseAuraContainer(slot)
    local created = CreateNameplateAuraContainer(slot, unit, config)
    if created then
        created.fuyutsuiUnit = unit
        auraContainers[slot] = created
    end
end

function Fuyutsui:ReleaseNameplateAuraContainers()
    for slot in pairs(auraContainers) do
        ReleaseAuraContainer(slot)
    end
end

function Fuyutsui:LoadNameplatePixels(config)
    self:ReleaseNameplateAuraContainers()
    rowCount = 0
    nameplateFrame:Hide()
    if type(config) ~= "table" then
        return
    end

    local healthRow = tonumber(config.healthPercent) or 0
    local rangeRow = tonumber(config.range) or 0
    local auraCount = type(config.auras) == "table" and #config.auras or 0
    local baseRow = math.max(healthRow, rangeRow, 2)
    rowCount = math.min(NAMEPLATE_MAX_ROWS, baseRow + auraCount)
    if rowCount <= 0 then
        return
    end

    nameplateFrame:ClearAllPoints()
    nameplateFrame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, GetNameplateTopOffset())
    nameplateFrame:SetHeight((rowCount + 1) * NAMEPLATE_CELL_HEIGHT)
    local markerRow = EnsureRow(1)
    markerRow[1]:SetColorTexture(NAMEPLATE_MARKER_R, 0, NAMEPLATE_MARKER_B, 1)
    markerRow[2]:SetColorTexture(0, 0, rowCount / 255, 1)
    for slot = 3, NAMEPLATE_SLOT_COUNT do
        markerRow[slot]:SetColorTexture(0, 0, 0, 1)
    end
    for row = 2, rowCount + 1 do
        EnsureRow(row)
    end
    nameplateFrame:Show()
end

function Fuyutsui:RefreshNameplatePixels()
    local config = self.blocks and self.blocks.nameplates
    if not config or rowCount <= 0 then
        return
    end

    local healthRow = tonumber(config.healthPercent) or 0
    local rangeRow = tonumber(config.range) or 0
    for slot = 1, NAMEPLATE_SLOT_COUNT do
        local unit = "nameplate" .. slot
        local hostile = UnitExists(unit) and UnitCanAttack("player", unit) and not UnitCanAssist("player", unit)
        if not hostile then
            ClearSlot(slot)
            ReleaseAuraContainer(slot)
        else
            ClearSlot(slot)
            if healthRow > 0 then
                local health = UnitHealthPercent(unit, false, self.curve100)
                local _, _, value = health:GetRGB()
                SetNormalizedCell(healthRow + 1, slot, true, value)
            end
            if rangeRow > 0 then
                local _, maxRange = self:GetUnitRangeBounds(unit)
                SetCell(rangeRow + 1, slot, true, maxRange or 0)
            end
            RefreshAuraContainer(slot, unit, config)
        end
    end
end

function Fuyutsui:ClearNameplatePixelSlot(unit)
    local slot = tonumber(string.match(unit or "", "^nameplate(%d+)$"))
    if not slot or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then
        return
    end
    ClearSlot(slot)
    ReleaseAuraContainer(slot)
end

Fuyutsui.NameplateSlotCount = NAMEPLATE_SLOT_COUNT
Fuyutsui.NameplateMaxRows = NAMEPLATE_MAX_ROWS
