-- Run from the repository root with Lua 5.1+ (or Python lupa).
local frames, pixels = {}, {}
local methods = {}
local function widget(parent)
    return setmetatable({ parent = parent, textures = {}, slots = {} }, { __index = methods })
end
function methods:SetPoint(...) self.point = { ... } end
function methods:SetSize(w, h) self.width, self.height = w, h end
function methods:SetFrameLevel(level) self.level = level end
function methods:GetFrameLevel() return self.level or 0 end
function methods:SetParent(parent) self.parent = parent end
function methods:SetEnabled(enabled) self.enabled = enabled end
function methods:SetUnit(unit) self.unit = unit end
function methods:Show() self.shown = true end
function methods:Hide() self.shown = false end
function methods:SetColorTexture(r, g, b, a) self.color = { r, g, b, a } end
function methods:SetDurationText(text, binding) self.binding = binding end
function methods:CreateTexture()
    local texture = widget(self)
    table.insert(self.textures, texture)
    if self == frames.FuyutsuiColorBars then pixels[#pixels + 1] = texture end
    return texture
end
function methods:CreateFontString() return widget(self) end
function methods:GetStatusBarTexture() return widget(self) end
function methods:AddAuraSlot(key, filter, config)
    local button = widget(self)
    config.initializeFrame(button)
    self.slots[key] = { button = button, config = config, filter = filter }
end
setmetatable(methods, { __index = function(_, key)
    if key:match("^[A-Z]") then return function() end end
end })
function CreateFrame(kind, name, parent)
    assert(name ~= "FuyutsuiNameplatePixels", "Separate nameplate frame must be removed")
    local frame = widget(parent)
    if name then frames[name] = frame end
    return frame
end
UIParent = widget()
function GetScreenWidth() return 1020 end
function CreateUnitHealPredictionCalculator() return {} end
function CreateColor(r, g, b, a) return { r, g, b, a } end
C_CurveUtil = { CreateColorCurve = function()
    return { points = {}, SetType = function() end, AddPoint = function(self, x, color) self.points[x] = color end }
end }
Enum = { LuaCurveType = { Linear = 1 }, DurationTextBindingProperty = { RemainingDuration = 1 } }
AuraContainerSortMethod = { Expiration = 1 }
AuraContainerSortDirection = { Normal = 1 }
tinsert = table.insert
Fuyutsui = {}
local exists = { nameplate1 = true, nameplate20 = true }
function UnitExists(unit) return exists[unit] or false end
function UnitCanAttack() return true end
function UnitCanAssist() return false end
local function forbidden() error("Secret health channel was inspected or calculated") end
local secret = setmetatable({}, { __add = forbidden, __sub = forbidden, __mul = forbidden,
    __div = forbidden, __lt = forbidden, __le = forbidden, __tostring = forbidden })
function UnitHealthPercent() return { GetRGB = function() return secret, secret, secret end } end
function Fuyutsui:GetUnitRangeBounds() return 5, 10 end

dofile("Fuyutsui/core/block.lua")
dofile("Fuyutsui/core/nameplates.lua")
dofile("Fuyutsui/main.lua")
Fuyutsui.ClassBlocks = { [1] = {
    states = { "锚点", "职业", "专精" },
    group = { num = 5, healthPercent = 1, role = 2 },
    nameplates = { healthPercent = 7, range = 9, auras = { { spellId = 589 }, { spellId = 34914 } } },
} }
Fuyutsui:LoadPlayerBlocks(1)
local config = Fuyutsui.blocks.nameplates
assert(config.start == 205 and config.num == 4 and config.auraStart == 3, "Layout must match C# converter")
assert(#pixels == 510, "Nameplates must reuse main textures")
assert(rawequal(pixels[205].color[3], secret), "Health must pass unchanged to SetColorTexture")
assert(pixels[206].color[3] == 10 / 255, "Range byte encoding changed")
assert(rawequal(pixels[281].color[3], secret), "20th unit offset is incorrect")
for i = 209, 212 do assert(pixels[i].color[1] == 0 and pixels[i].color[2] == 0, "Absent unit retains an index") end

for _, slot in ipairs({ 1, 20 }) do
    local container = frames["FuyutsuiNameplateAuraSlots_" .. slot]
    local count = 0
    for _, entry in pairs(container.slots) do
        count = count + 1
        local button = entry.button
        local firstAura = 207 + (slot - 1) * 4
        local x = button.point[4]
        assert(button.point[5] == 0 and button.width == 2 and button.height == 1, "Aura must use main-row position and size")
        local index = x / 2 + 1
        assert(index == firstAura or index == firstAura + 1, "Aura overlaps another unit")
        local color = button.binding and button.binding.textColor.curve.points[255] or button.textures[1].color
        assert(color[1] == (index > 255 and 1 / 255 or 0), "Aura R index channel is wrong")
        assert(color[2] == (index > 255 and index - 255 or index) / 255, "Aura G index channel is wrong")
    end
    assert(count == 4, "Two auras must each reuse timed and permanent slots")
end

local oldContainer = frames.FuyutsuiNameplateAuraSlots_1
Fuyutsui:ClearNameplatePixelSlot("nameplate1")
assert(oldContainer.enabled == false and oldContainer.shown == false, "Removal leaves aura overlays")
for i = 205, 208 do assert(pixels[i].color[2] == 0, "Removal leaves a main-row index") end
assert(pixels[204].color[2] == 204 / 255, "Nameplate clearing overlaps the final group pixel")
assert(rawequal(pixels[281].color[3], secret), "Clearing one unit changes another")

Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" }, nameplates = { auras = { { spellId = 589 } } } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates.start == 4 and Fuyutsui.blocks.nameplates.num == 1, "Aura-only config must use 20 pixels")
assert(pixels[205].color[2] == 205 / 255 and pixels[205].color[3] == 0, "Old allocation was not reset")
Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" }, nameplates = { range = 8 } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates.num == 1 and Fuyutsui.blocks.nameplates.range == 1, "Range-only offset must compact")
Fuyutsui.ClassBlocks[1].nameplates = nil
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates == nil, "Disabled config retains allocation")
print("PASS: Lua layout, main texture reuse, secret channel passthrough, aura position/index encoding, removal and config reload.")
