local addon, ns = ...
local format = string.format
local macroList = {}
local macroKind = {}
local bindingOwner = CreateFrame("Frame")

local modifiers = {
    "RCTRL", "RALT", "RSHIFT",
    "RALT-RCTRL", "RALT-RSHIFT", "RCTRL-RSHIFT",
    "RALT-RCTRL-RSHIFT",
    "LCTRL", "LALT", "LSHIFT",
    "LALT-LCTRL", "LALT-LSHIFT", "LCTRL-LSHIFT",
    "LALT-LCTRL-LSHIFT",
    "LALT-RCTRL", "RALT-LCTRL",
    "LALT-RSHIFT", "RALT-LSHIFT",
    "LCTRL-RSHIFT", "RCTRL-LSHIFT",
    "LALT-LCTRL-RSHIFT", "LALT-RCTRL-LSHIFT", "LALT-RCTRL-RSHIFT",
    "RALT-LCTRL-LSHIFT", "RALT-LCTRL-RSHIFT", "RALT-RCTRL-LSHIFT"
}

local keys = {
    "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5",
    "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9", "NUMPAD0",
    "NUMPADDECIMAL", "NUMPADPLUS", "NUMPADMINUS", "NUMPADMULTIPLY", "NUMPADDIVIDE",
    "F1", "F2", "F3", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    ",", ".", ";", "'", "[", "]", "\\", "=", "-",
    "INSERT", "HOME", "END", "PAGEUP", "PAGEDOWN",
    "UP", "DOWN", "LEFT", "RIGHT"
}

do
    local i = 1
    for _, m in ipairs(modifiers) do
        for _, k in ipairs(keys) do
            macroKind[i] = m .. "-" .. k
            i = i + 1
        end
    end
end


local function createMacro(name, key, macro)
    if InCombatLockdown() then
        -- print("|cFFFF0000错误：战斗中不能创建按钮|r")
        return
    end
    local btn = macroList[name]
    if not btn then
        btn = CreateFrame("Button", name, UIParent, "SecureActionButtonTemplate")
        btn:SetAttribute("type", "macro")
        btn:RegisterForClicks("AnyUp", "AnyDown")
        macroList[name] = btn
    end
    SetOverrideBindingClick(bindingOwner, true, key, name, "LeftButton")
    btn:SetAttribute("macrotext", macro)
    -- print(name, key, macro)
end

-- 解析法术名/宏体：优先查 MacroBodies；以 / 开头则原样使用；否则加 /cast
local function resolveMacroBody(spell)
    if not spell or spell == "" then
        return nil
    end
    local bodies = Fuyutsui.MacroBodies
    local body = bodies and bodies[spell]
    if body then
        if body:sub(1, 1) == "/" then
            return body
        end
        return "/cast " .. body
    end
    if spell:sub(1, 1) == "/" then
        return spell
    end
    return "/cast " .. spell
end

function Fuyutsui:ClearMacros()
    if InCombatLockdown() then
        return
    end
    ClearOverrideBindings(bindingOwner)
    for _, btn in pairs(macroList) do
        btn:SetAttribute("macrotext", nil)
    end
end

function Fuyutsui:CreateMacro(dynamicData, staticData, specialData)
    dynamicData = dynamicData or {}
    staticData = staticData or {}
    specialData = specialData or {}

    self:ClearMacros()

    local i = 1
    local function nextSlot(macroBody)
        local keyBinding = macroKind[i]
        if not keyBinding then
            return
        end
        if macroBody then
            createMacro("s" .. i, keyBinding, macroBody)
        end
        i = i + 1
    end

    -- 1. dynamicSpells：每组占 30 个键（raid/party 展开）
    for _, spell in ipairs(dynamicData) do
        for raidIdx = 1, 30 do
            local macroBody
            if spell and spell ~= "" then
                if raidIdx == 1 then
                    macroBody = format("/cast [group:raid,@raid1]%s;[group:party,@player]%s;[nogroup,@player]%s", spell,
                        spell,
                        spell)
                elseif raidIdx <= 5 then
                    macroBody = format("/cast [group:raid,@raid%d]%s;[group:party,@party%d]%s", raidIdx, spell,
                        raidIdx - 1, spell)
                else
                    macroBody = format("/cast [group:raid,@raid%d]%s", raidIdx, spell)
                end
            end
            nextSlot(macroBody)
        end
    end

    -- 2. staticSpells：依次占键；空字符串保留占位但不创建
    for _, spell in ipairs(staticData) do
        nextSlot(resolveMacroBody(spell))
    end

    -- 3. specialSpells：完整宏文本，接在 static 之后依次占键
    for _, spell in ipairs(specialData) do
        nextSlot(resolveMacroBody(spell))
    end
end
