local addon, ns = ...

-- 姓名板主像素：先 7 格单位映射（目标/焦点/首领1–5），再每单位 num 格（生命值、距离、战斗、光环及可选状态）。
-- 槽位像素：index = start + mappingCount + (slot - 1) * num + offset - 1；沿用主像素 R/G 索引与 B 数值。
-- 不存在的单位整段置黑（无索引），无需增加存在标记格。
local NAMEPLATE_SLOT_COUNT = 40
local NAMEPLATE_MAPPING_COUNT = 7
local MAPPING_UNITS = {
    "target",
    "focus",
    "boss1",
    "boss2",
    "boss3",
    "boss4",
    "boss5",
}

local auraContainers = {}
local pixelConfig

local function PixelIndex(config, slot, offset)
    return config.start + NAMEPLATE_MAPPING_COUNT + (slot - 1) * config.num + offset - 1
end

local function MappingPixelIndex(config, mappingIndex)
    return config.start + mappingIndex - 1
end

local function RegionEnd(config)
    return config.start + NAMEPLATE_MAPPING_COUNT + NAMEPLATE_SLOT_COUNT * config.num - 1
end

local function ClearSlot(slot)
    if not pixelConfig then return end
    if pixelConfig.castSpellOffset then
        Shingen:ClearRgbSpellPixel(PixelIndex(pixelConfig, slot, pixelConfig.castSpellOffset))
    end
    for offset = 1, pixelConfig.num do
        Shingen:ClearNameplateTexture(PixelIndex(pixelConfig, slot, offset))
    end
end

local function ReleaseAuraContainer(slot)
    local container = auraContainers[slot]
    if not container then return end
    container:SetEnabled(false)
    container:Hide()
    container:SetParent(nil)
    auraContainers[slot] = nil
end

local function CreateNameplateAuraContainer(slot, unit, config)
    if #config.auras == 0 then return nil end

    if C_AddOns and not C_AddOns.IsAddOnLoaded("Blizzard_AuraContainer") then
        C_AddOns.LoadAddOn("Blizzard_AuraContainer")
    end
    local container = CreateFrame(
        "AuraContainer", "ShingenNameplateAuraSlots_" .. slot, UIParent, "CustomAuraContainerTemplate"
    )
    container:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
    container:SetUnit(unit)
    container:SetEnabled(true)
    container:SetFrameStrata("TOOLTIP")
    container:SetFrameLevel(9003)

    for auraIndex, aura in ipairs(config.auras) do
        local includeSpellIDs = {}
        if aura.spellId then includeSpellIDs[aura.spellId] = true end
        if type(aura.spellIds) == "table" then
            for _, spellId in ipairs(aura.spellIds) do
                includeSpellIDs[spellId] = true
            end
        end
        local index = PixelIndex(config, slot, aura.valueOffset)
        Shingen:AddNameplateAuraPixelSlots(
            container, "nameplate_" .. slot .. "_aura_" .. auraIndex, includeSpellIDs, index, aura.isPlayer, aura.maxApps
        )
    end
    container:Show()
    return container
end

local function RefreshAuraContainer(slot, unit, config)
    if #config.auras == 0 then
        ReleaseAuraContainer(slot)
        return
    end
    local current = auraContainers[slot]
    if current then
        current:SetUnit(unit)
        current:SetEnabled(true)
        current:Show()
        return
    end
    auraContainers[slot] = CreateNameplateAuraContainer(slot, unit, config)
end

-- UnitIsUnit 结果不可读（密钥值）或未匹配时返回 0。
local function FindNameplateSlot(unit)
    if type(unit) ~= "string" or unit == "" then
        return 0
    end
    if not UnitExists(unit) then
        return 0
    end

    for slot = 1, NAMEPLATE_SLOT_COUNT do
        local isSame = UnitIsUnit(unit, "nameplate" .. slot)
        if issecretvalue and issecretvalue(isSame) then
            -- 该对不可读，继续找可读匹配；全部不可读则最终为 0。
        elseif isSame then
            return slot
        end
    end
    return 0
end

function Shingen:ReleaseNameplateAuraContainers()
    for slot in pairs(auraContainers) do ReleaseAuraContainer(slot) end
end

function Shingen:RefreshNameplateUnitMappings()
    local config = pixelConfig
    if not config or not config.start then return end

    for mappingIndex = 1, NAMEPLATE_MAPPING_COUNT do
        local slot = FindNameplateSlot(MAPPING_UNITS[mappingIndex])
        self:CreateTexture(MappingPixelIndex(config, mappingIndex), slot / 255)
    end
end

function Shingen:LoadNameplatePixels(config)
    self:ReleaseNameplateAuraContainers()
    -- 旧分配可能已被新专精使用，恢复普通零值索引。
    if pixelConfig then
        if pixelConfig.castSpellOffset then
            for slot = 1, NAMEPLATE_SLOT_COUNT do
                self:ClearRgbSpellPixel(PixelIndex(pixelConfig, slot, pixelConfig.castSpellOffset))
            end
        end
        for index = pixelConfig.start, RegionEnd(pixelConfig) do
            self:CreateTexture(index, 0)
        end
    end
    pixelConfig = config
    if not pixelConfig then return end
    for mappingIndex = 1, NAMEPLATE_MAPPING_COUNT do
        self:CreateTexture(MappingPixelIndex(pixelConfig, mappingIndex), 0)
    end
    for slot = 1, NAMEPLATE_SLOT_COUNT do ClearSlot(slot) end
    self:RefreshNameplateUnitMappings()
end

function Shingen:RefreshNameplatePixels()
    local config = pixelConfig
    if not config or config.num <= 0 then return end

    self:RefreshNameplateUnitMappings()

    for slot = 1, NAMEPLATE_SLOT_COUNT do
        local unit = "nameplate" .. slot
        local hostile = UnitExists(unit) and not UnitIsFriend("player", unit)
        if not hostile then
            ReleaseAuraContainer(slot)
            ClearSlot(slot)
        else
            -- 即使所有数值都是 0，索引仍表示该单位存在。
            for offset = 1, config.num do
                self:CreateTexture(PixelIndex(config, slot, offset), 0)
            end
            if config.healthPercent then
                local health = UnitHealthPercent(unit, false, self.curve100)
                local _, _, value = health:GetRGB()
                -- 已归一化的秘密颜色通道原样传给主像素，禁止计算、比较或布尔判断。
                self:CreateTexture(PixelIndex(config, slot, config.healthPercent), value)
            end
            if config.range then
                local maxRange = 0
                if UnitCanAttack("player", unit) then
                    maxRange = select(2, self:GetUnitRangeBounds(unit))
                end
                self:CreateTexture(PixelIndex(config, slot, config.range),
                    math.max(0, math.min(255, maxRange or 0)) / 255)
            end
            if config.combat then
                -- 战斗状态只有 0/1 两种取值，用 1/255 表示「战斗中」。
                self:CreateTexture(PixelIndex(config, slot, config.combat), UnitAffectingCombat(unit) and 1 / 255 or 0)
            end
            if config.threatOffset then
                local status = UnitThreatSituation("player", unit)
                -- 无仇恨记录时 API 返回 nil，与状态 0 一并表示未坦克该单位。
                self:CreateTexture(PixelIndex(config, slot, config.threatOffset), (status or 0) / 255)
            end
            if config.castSpellOffset then
                self:RefreshRgbSpellPixel(PixelIndex(config, slot, config.castSpellOffset), unit)
            end
            if config.castCountdownOffset then
                self:CreateTexture(PixelIndex(config, slot, config.castCountdownOffset),
                    self:GetUnitCastCountdownPixel(unit))
            end
            RefreshAuraContainer(slot, unit, config)
        end
    end
end

function Shingen:RefreshNameplateCastPixel(unit)
    local config = pixelConfig
    if not config or not (config.castSpellOffset or config.castCountdownOffset) then return end
    local slot = tonumber(string.match(unit or "", "^nameplate(%d+)$"))
    if not slot or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then return end
    local hostile = UnitExists(unit) and not UnitIsFriend("player", unit)
    if config.castSpellOffset then
        local index = PixelIndex(config, slot, config.castSpellOffset)
        if hostile then
            self:RefreshRgbSpellPixel(index, unit)
        else
            self:ClearRgbSpellPixel(index)
            self:ClearNameplateTexture(index)
            self:ClearNameplateTexture(index + 1)
        end
    end
    if config.castCountdownOffset then
        local index = PixelIndex(config, slot, config.castCountdownOffset)
        if hostile then
            self:CreateTexture(index, self:GetUnitCastCountdownPixel(unit))
        else
            self:ClearNameplateTexture(index)
        end
    end
end

function Shingen:ClearNameplatePixelSlot(unit)
    local slot = tonumber(string.match(unit or "", "^nameplate(%d+)$"))
    if not slot or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then return end
    ReleaseAuraContainer(slot)
    ClearSlot(slot)
end

Shingen.NameplateSlotCount = NAMEPLATE_SLOT_COUNT
Shingen.NameplateMappingFieldCount = NAMEPLATE_MAPPING_COUNT
