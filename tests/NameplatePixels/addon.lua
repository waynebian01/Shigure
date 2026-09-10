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
function GetScreenWidth() return 1022 end -- 511 格（510 数据 + 1 终止）每格恰好 2 像素
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
local inCombat = { nameplate1 = true }
function UnitAffectingCombat(unit) return inCombat[unit] or false end
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
    group = { state = { "healthPercent", "role", "dispel" }, aura = { { spellId = 194384 }, { spellIds = { 17, 1253593 } } } },
    nameplates = { auras = { { spellId = 589 }, { spellId = 34914 } } },
} }
Fuyutsui:LoadPlayerBlocks(1)
local config = Fuyutsui.blocks.nameplates
assert(Fuyutsui.blocks.groups.num == 5 and Fuyutsui.blocks.groups.aura[4].spellId == 194384,
    "Group fields and plain aura list must auto-allocate five pixels")
assert(config.start == 155 and config.num == 5 and config.auraStart == 4, "Layout must match C# converter")
assert(#pixels == 511, "Nameplates must reuse main textures")
assert(Fuyutsui.MainPixelCount == 510, "Default capacity tier changed")
local endColor = pixels[511].color
assert(endColor[1] == 1 and endColor[2] == 0 and endColor[3] == 0, "Main row must end with a pure red terminator")
assert(rawequal(pixels[155].color[3], secret), "Health must pass unchanged to SetColorTexture")
assert(pixels[156].color[3] == 10 / 255, "Range byte encoding changed")
assert(pixels[157].color[3] == 1 / 255, "Combat pixel must encode UnitAffectingCombat as 1")
assert(rawequal(pixels[250].color[3], secret), "20th unit offset is incorrect")
assert(pixels[252].color[3] == 0, "Out-of-combat unit must clear the combat pixel")
for i = 160, 164 do assert(pixels[i].color[1] == 0 and pixels[i].color[2] == 0, "Absent unit retains an index") end

for _, slot in ipairs({ 1, 20 }) do
    local container = frames["FuyutsuiNameplateAuraSlots_" .. slot]
    local count = 0
    for _, entry in pairs(container.slots) do
        count = count + 1
        local button = entry.button
        local firstAura = 158 + (slot - 1) * 5
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
for i = 155, 159 do assert(pixels[i].color[2] == 0, "Removal leaves a main-row index") end
assert(pixels[154].color[2] == 154 / 255, "Nameplate clearing overlaps the final group pixel")
assert(rawequal(pixels[250].color[3], secret), "Clearing one unit changes another")

Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" }, nameplates = { auras = { { spellId = 589 } } } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates.start == 4 and Fuyutsui.blocks.nameplates.num == 4, "Aura-only config must use 80 pixels")
assert(pixels[155].color[2] == 155 / 255 and pixels[155].color[3] == 0, "Old allocation was not reset")
Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" }, nameplates = {} }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates.num == 3 and Fuyutsui.blocks.nameplates.healthPercent == 1
    and Fuyutsui.blocks.nameplates.range == 2 and Fuyutsui.blocks.nameplates.combat == 3,
    "Aura-less config must still reserve the three fixed fields")
Fuyutsui.ClassBlocks[1].nameplates = nil
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.nameplates == nil, "Disabled config retains allocation")
print("PASS: Lua layout, main texture reuse, secret channel passthrough, aura position/index encoding, removal and config reload.")

Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" },
    group = { num = 99, role = 8, aura = { [10] = { spellId = 17 }, [4] = { spellId = 194384 } } },
    nameplates = { range = 1 } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups.num == 3 and Fuyutsui.blocks.groups.role == 1
    and Fuyutsui.blocks.groups.aura[2].spellId == 194384
    and Fuyutsui.blocks.nameplates.start == 95, "Legacy group config must compact and move nameplates")
Fuyutsui.state, Fuyutsui.roleMap, Fuyutsui.groupHealthCurves = {}, {}, {}
Fuyutsui.group = { player = { index = 1 } }
dofile("Fuyutsui/unit/group.lua")
Fuyutsui:RefreshGroupMemberHealth("player") -- health field disabled: no API call or nil arithmetic
Fuyutsui.ClassBlocks[1].group = { aura = { { spellId = 194384 } } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups.num == 1 and Fuyutsui.blocks.groups.aura[1].spellId == 194384)
Fuyutsui:RefreshNextGroupMemberState() -- role disabled: no nil arithmetic
Fuyutsui.ClassBlocks[1].group = {}
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups == nil and Fuyutsui.blocks.nameplates.start == 4, "Empty group must reserve no pixels")
print("PASS: automatic group offsets, legacy sparse lists, disabled health/role and empty group.")

Fuyutsui.ClassBlocks[1].group = { state = { "dispel", "role", "healthPercent" }, healthPercent = 99,
    aura = { { spellId = 194384 } } }
Fuyutsui:LoadPlayerBlocks(1)
local groupConfig = Fuyutsui.blocks.groups
assert(groupConfig.dispel == 1 and groupConfig.role == 2 and groupConfig.healthPercent == 3
    and groupConfig.aura[4].spellId == 194384 and groupConfig.num == 4, "Explicit state order must control pixels")
Fuyutsui.ClassBlocks[1].group = { state = {}, healthPercent = 1, role = 2 }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups == nil, "Empty state must not fall back to legacy fields")
Fuyutsui.ClassBlocks[1].group = { state = { "role", "role", "unknown", "healthPercent" } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups.num == 2 and Fuyutsui.blocks.groups.role == 1
    and Fuyutsui.blocks.groups.healthPercent == 2, "Unknown and duplicate fields must not allocate pixels")
print("PASS: explicit state ordering/precedence, empty lists and duplicate filtering.")

-- 残留的 state 与旧偏移都不再影响布局：生命值/距离/战斗恒为第 1、2、3 格。
for _, plates in ipairs({
    { state = { "range", "range", "unknown", "healthPercent" }, healthPercent = 99, auras = { { spellId = 589 } } },
    { state = {}, healthPercent = 1, range = 2, auras = { { spellId = 589 } } },
    { healthPercent = 0, range = 4, auras = { { spellId = 589 } } },
}) do
    Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" }, nameplates = plates }
    Fuyutsui:LoadPlayerBlocks(1)
    local plateConfig = Fuyutsui.blocks.nameplates
    assert(plateConfig.healthPercent == 1 and plateConfig.range == 2 and plateConfig.combat == 3
        and plateConfig.auraStart == 4 and plateConfig.num == 4,
        "Nameplate fixed fields must ignore state lists and legacy offsets")
end
print("PASS: hardcoded nameplate health/range/combat offsets ignore state lists and legacy fields.")

-- 容量分档：17 个队伍字段占到 514 格，超过 510 后升到 765 档，末尾终止格跟着后移。
local manyAuras = {}
for i = 1, 16 do manyAuras[i] = { spellId = 100000 + i } end
Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" },
    group = { state = { "healthPercent" }, aura = manyAuras } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.blocks.groups.num == 17, "Group fixture must reserve 17 fields")
assert(Fuyutsui.MainPixelCount == 765 and #pixels == 766, "Exceeding 510 data blocks must switch to the 765 tier")
assert(pixels[511].color[1] == 2 / 255 and pixels[511].color[2] == 1 / 255, "Third index scheme must use r=2/255")
assert(pixels[765].color[1] == 2 / 255 and pixels[765].color[2] == 255 / 255, "Tier 765 must end the third scheme")
assert(pixels[766].color[1] == 1 and pixels[766].color[2] == 0 and pixels[766].color[3] == 0,
    "Terminator must move to the end of the active tier")
assert(pixels[600].width == 1022 / 766, "Block width must follow the active tier")

-- 回到默认档：多余纹理必须隐藏，终止格回到 511。
Fuyutsui.ClassBlocks[1] = { states = { "锚点", "职业", "专精" } }
Fuyutsui:LoadPlayerBlocks(1)
assert(Fuyutsui.MainPixelCount == 510 and #pixels == 766, "Shrinking must reuse textures instead of creating more")
assert(pixels[511].color[1] == 1 and pixels[511].color[2] == 0 and pixels[511].color[3] == 0,
    "Terminator must return to the default tier end")
assert(pixels[512].shown == false and pixels[766].shown == false, "Blocks beyond the tier must be hidden")
assert(pixels[510].shown ~= false and pixels[510].width == 1022 / 511, "Active blocks must be re-anchored")
print("PASS: capacity tiers 510/765/1020, third index scheme and the constant red terminator.")
