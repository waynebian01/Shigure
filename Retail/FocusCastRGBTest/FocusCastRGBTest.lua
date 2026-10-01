-- 独立测试插件：用一个 RGB 方块显示焦点施法或引导的 spellID。
-- 深灰表示当前没有可用的施法或引导数据。
local frame = CreateFrame("Frame", "FocusCastRGBTestFrame", UIParent)
frame:SetSize(48, 48)
frame:SetPoint("CENTER", UIParent, "CENTER")
frame:SetFrameStrata("TOOLTIP")
frame:SetFrameLevel(9000)
frame:SetClipsChildren(true)
frame:EnableMouse(false)

local square = frame:CreateFontString(nil, "ARTWORK", "GameFontNormal")
square:SetPoint("CENTER", frame, "CENTER")
square:SetFontHeight(88)
square:SetTextColor(1, 1, 1)
square:SetFixedColor(false)

local NO_CAST_TEXT = "|cFF404040█|r"

local function PrintSpell(kind, spellID)
    local secret = issecretvalue(spellID)
    local formatted, displayID = pcall(string.format, "%d", spellID)
    if not formatted then
        print("[FocusCastRGBTest]", kind, "无可用 spellID，secret:", secret)
        return
    end

    -- 某些受限制状态可能拒绝把秘密值写入聊天，避免因此中断色块刷新。
    local printed = pcall(print, "[FocusCastRGBTest]", kind, "spellID:", spellID, "secret:", secret)
    if not printed then
        printed = pcall(print, "[FocusCastRGBTest]", kind, "spellID:", displayID, "secret:", secret)
    end
    if not printed then
        print("[FocusCastRGBTest]", kind, "聊天拒绝显示 spellID，secret:", secret)
    end
end

local function Refresh()
    local _, _, _, _, _, _, _, _, castingSpellID = UnitCastingInfo("focus")
    -- pcall 同时处理无施法时的 nil。不要在 Lua 中比较或拆分可能为秘密值的 ID。
    local ok, coloredText = pcall(string.format, "|cFF%06X█|r", castingSpellID)
    if ok then
        square:SetText(coloredText)
        return "施法", castingSpellID
    end

    local _, _, _, _, _, _, _, channelSpellID = UnitChannelInfo("focus")
    ok, coloredText = pcall(string.format, "|cFF%06X█|r", channelSpellID)
    if ok then
        square:SetText(coloredText)
        return "引导", channelSpellID
    end

    square:SetText(NO_CAST_TEXT)
end

local function OnEvent(_, event)
    local kind, currentSpellID = Refresh()
    if event == "UNIT_SPELLCAST_START" or event == "UNIT_SPELLCAST_EMPOWER_START" then
        local _, _, _, _, _, _, _, _, castingSpellID = UnitCastingInfo("focus")
        PrintSpell("施法", castingSpellID)
    elseif event == "UNIT_SPELLCAST_CHANNEL_START" then
        local _, _, _, _, _, _, _, channelSpellID = UnitChannelInfo("focus")
        PrintSpell("引导", channelSpellID)
    elseif event == "PLAYER_FOCUS_CHANGED" and kind then
        PrintSpell(kind, currentSpellID)
    end
end

frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("PLAYER_FOCUS_CHANGED")

local castEvents = {
    "UNIT_SPELLCAST_START",
    "UNIT_SPELLCAST_STOP",
    "UNIT_SPELLCAST_FAILED",
    "UNIT_SPELLCAST_FAILED_QUIET",
    "UNIT_SPELLCAST_INTERRUPTED",
    "UNIT_SPELLCAST_DELAYED",
    "UNIT_SPELLCAST_SUCCEEDED",
    "UNIT_SPELLCAST_CHANNEL_START",
    "UNIT_SPELLCAST_CHANNEL_STOP",
    "UNIT_SPELLCAST_EMPOWER_START",
    "UNIT_SPELLCAST_EMPOWER_STOP",
}

for _, event in ipairs(castEvents) do
    frame:RegisterUnitEvent(event, "focus")
end

frame:SetScript("OnEvent", OnEvent)
Refresh()
