local addon, ns = ...

-- 接在队伍后面的主像素：每单位 num 格，依次为已配置的生命值、距离、战斗、光环。
-- index = start + (slot - 1) * num + offset - 1；沿用主像素 R/G 索引与 B 数值。
-- 不存在的单位整段置黑（无索引），无需增加存在标记格。
local NAMEPLATE_SLOT_COUNT = 20
local auraContainers = {}
local pixelConfig

local function PixelIndex(config, slot, offset)
    return config.start + (slot - 1) * config.num + offset - 1
end

local function ClearSlot(slot)
    if not pixelConfig then return end
    for offset = 1, pixelConfig.num do
        Fuyutsui:ClearNameplateTexture(PixelIndex(pixelConfig, slot, offset))
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
        "AuraContainer", "FuyutsuiNameplateAuraSlots_" .. slot, UIParent, "CustomAuraContainerTemplate"
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
        local index = PixelIndex(config, slot, config.auraStart + auraIndex - 1)
        Fuyutsui:AddNameplateAuraPixelSlots(
            container, "nameplate_" .. slot .. "_aura_" .. auraIndex, includeSpellIDs, index
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

function Fuyutsui:ReleaseNameplateAuraContainers()
    for slot in pairs(auraContainers) do ReleaseAuraContainer(slot) end
end

function Fuyutsui:LoadNameplatePixels(config)
    self:ReleaseNameplateAuraContainers()
    -- 旧分配可能已被新专精使用，恢复普通零值索引。
    if pixelConfig then
        for index = pixelConfig.start, pixelConfig.start + NAMEPLATE_SLOT_COUNT * pixelConfig.num - 1 do
            self:CreateTexture(index, 0)
        end
    end
    pixelConfig = config
    for slot = 1, NAMEPLATE_SLOT_COUNT do ClearSlot(slot) end
end

function Fuyutsui:RefreshNameplatePixels()
    local config = pixelConfig
    if not config or config.num <= 0 then return end

    for slot = 1, NAMEPLATE_SLOT_COUNT do
        local unit = "nameplate" .. slot
        local hostile = UnitExists(unit) and UnitCanAttack("player", unit) and not UnitCanAssist("player", unit)
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
                local _, maxRange = self:GetUnitRangeBounds(unit)
                self:CreateTexture(PixelIndex(config, slot, config.range), math.max(0, math.min(255, maxRange or 0)) / 255)
            end
            if config.combat then
                -- 战斗状态只有 0/1 两种取值，用 1/255 表示「战斗中」。
                self:CreateTexture(PixelIndex(config, slot, config.combat), UnitAffectingCombat(unit) and 1 / 255 or 0)
            end
            RefreshAuraContainer(slot, unit, config)
        end
    end
end

function Fuyutsui:ClearNameplatePixelSlot(unit)
    local slot = tonumber(string.match(unit or "", "^nameplate(%d+)$"))
    if not slot or slot < 1 or slot > NAMEPLATE_SLOT_COUNT then return end
    ReleaseAuraContainer(slot)
    ClearSlot(slot)
end

Fuyutsui.NameplateSlotCount = NAMEPLATE_SLOT_COUNT
