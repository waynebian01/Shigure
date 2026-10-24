local addon, ns = ...

local fuDelayEndTimer = nil

function Shingen:GetCharConfig()
    return self.db and self.db.char
end

function Shingen:NormalizeCharConfig()
    local c = self:GetCharConfig()
    if not c then return end
    c.cooldowns = c.cooldowns or 0
    c.minorCooldowns = c.minorCooldowns or 0
    c.customMode = c.customMode or 0
    c.delay = c.delay or 0
    c.potion = c.potion or 0
end

--- 通用角色开关：规范化、同步像素、刷新快捷按钮
function Shingen:SwitchCharFlag(key, offMsg, onMsg, blockName)
    local c = self:GetCharConfig()
    if not c then return end
    if c[key] == 0 then
        print(offMsg)
    else
        print(onMsg)
    end
    if blockName and self.UpdateBareStateBlock then
        self:UpdateBareStateBlock(blockName, { "配置开关", "状态" })
    end
    self:NormalizeCharConfig()
    if self.RefreshQuickToggleAppearance then
        self:RefreshQuickToggleAppearance()
    end
end

function Shingen:SwitchCooldown()
    self:SwitchCharFlag(
        "cooldowns",
        "|cff00ff00[Shingen]|r 爆发已|cffff0000关闭|r",
        "|cff00ff00[Shingen]|r 爆发已|cff00ff00开启|r",
        "爆发开关"
    )
end

function Shingen:SwitchPotion()
    self:SwitchCharFlag(
        "potion",
        "|cff00ff00[Shingen]|r 药水已|cffff0000关闭|r",
        "|cff00ff00[Shingen]|r 药水已|cff00ff00开启|r",
        "爆发药水开关"
    )
end

function Shingen:SwitchDelay()
    local c = self:GetCharConfig()
    if not c then return end
    if self.UpdateBareStateBlock then
        self:UpdateBareStateBlock("延迟", { "配置开关", "状态" })
    end
    self:NormalizeCharConfig()
end

local fuTimerTicker = nil
local fuTimerElapsed = 0

function Shingen:IsTimerRunning()
    return fuTimerTicker ~= nil
end

function Shingen:GetTimerElapsed()
    return fuTimerElapsed
end

local function SyncTimerPixel()
    if Shingen.UpdateStateBlock then
        Shingen:UpdateStateBlock("特殊", "计时器")
    end
    if Shingen.RefreshTimerAppearance then
        Shingen:RefreshTimerAppearance()
    end
end

--- 开始（或重新开始）秒表：立即显示 0，之后每秒 +1；超过 255 秒自动关闭
function Shingen:StartTimer()
    if fuTimerTicker then
        fuTimerTicker:Cancel()
        fuTimerTicker = nil
    end
    fuTimerElapsed = 0
    self.state.timer = 0
    SyncTimerPixel()
    print("|cff00ff00[Shingen]|r 计时器已开启")
    fuTimerTicker = C_Timer.NewTicker(1, function()
        fuTimerElapsed = fuTimerElapsed + 1
        if fuTimerElapsed > 255 then
            Shingen:StopTimer("overflow")
            return
        end
        Shingen.state.timer = fuTimerElapsed / 255
        SyncTimerPixel()
    end)
end

--- reason == "overflow" 时使用超时文案；右击 / /sh timer off 走默认关闭文案
function Shingen:StopTimer(reason)
    if fuTimerTicker then
        fuTimerTicker:Cancel()
        fuTimerTicker = nil
    end
    fuTimerElapsed = 0
    self.state.timer = 0
    SyncTimerPixel()
    if reason == "overflow" then
        print("|cff00ff00[Shingen]|r 计时器已超过 255 秒，已关闭")
    else
        print("|cff00ff00[Shingen]|r 计时器已关闭")
    end
end

function Shingen:ToggleTimer()
    if self:IsTimerRunning() then
        self:StopTimer()
    else
        self:StartTimer()
    end
end

local fuLoopTicker = nil
local fuLoopValue = 0
local fuLoopPeriod = 0

function Shingen:IsLoopTimerRunning()
    return fuLoopTicker ~= nil
end

local function SyncLoopTimerPixel()
    if Shingen.UpdateStateBlock then
        Shingen:UpdateStateBlock("特殊", "循环计时器")
    end
end

--- 从 1 计到 period 再回到 1；period 钳在 1..255
function Shingen:StartLoopTimer(period)
    period = math.floor(tonumber(period) or 0)
    if period < 1 then
        self:StopLoopTimer()
        return
    end
    if period > 255 then
        period = 255
    end
    if fuLoopTicker then
        fuLoopTicker:Cancel()
        fuLoopTicker = nil
    end
    fuLoopPeriod = period
    fuLoopValue = 1
    self.state.loopTimer = fuLoopValue / 255
    SyncLoopTimerPixel()
    print("|cff00ff00[Shingen]|r 循环计时器已开启（1-" .. period .. " 秒）")
    fuLoopTicker = C_Timer.NewTicker(1, function()
        fuLoopValue = fuLoopValue + 1
        if fuLoopValue > fuLoopPeriod then
            fuLoopValue = 1
        end
        Shingen.state.loopTimer = fuLoopValue / 255
        SyncLoopTimerPixel()
    end)
end

function Shingen:StopLoopTimer()
    if fuLoopTicker then
        fuLoopTicker:Cancel()
        fuLoopTicker = nil
    end
    fuLoopValue = 0
    fuLoopPeriod = 0
    self.state.loopTimer = 0
    SyncLoopTimerPixel()
    print("|cff00ff00[Shingen]|r 循环计时器已关闭")
end

function Shingen:ToggleLoopTimer()
    if self:IsLoopTimerRunning() then
        self:StopLoopTimer()
    else
        self:StartLoopTimer(255)
    end
end

local function FindSpellListByName(spellName)
    local list = Shingen.spellsList
    if not list then return nil end
    for spellId, info in pairs(list) do
        if type(info) == "table" and info.name == spellName and info.index then
            return info.index, spellId, info.name
        end
    end
    return nil
end

local function FindSpellListById(spellId)
    local list = Shingen.spellsList
    if not list then return nil end
    local info = list[spellId]
    if type(info) == "table" and info.index and info.name then
        return info.index, spellId, info.name
    end
    return nil
end

local function FindItemListByName(itemName)
    local list = Shingen.itemsList
    if not list then return nil end
    for itemId, info in pairs(list) do
        if type(info) == "table" and info.name == itemName and info.index then
            return info.index, itemId, info.name
        end
    end
    return nil
end

local function FindItemListById(itemId)
    local list = Shingen.itemsList
    if not list then return nil end
    local info = list[itemId]
    if type(info) == "table" and info.index and info.name then
        return info.index, itemId, info.name
    end
    return nil
end

function Shingen:InsertSpellCommand(rest)
    rest = strtrim(rest or "")
    if rest == "" then
        print("|cff00ff00[Shingen]|r 用法: /sh i 技能名称或spellId")
        return
    end

    local spellInput = rest

    local index, spellId, spellName
    if spellInput:match("^%d+$") then
        local requestedSpellId = tonumber(spellInput)
        if requestedSpellId and requestedSpellId > 0 then
            index, spellId, spellName = FindSpellListById(requestedSpellId)
        end
    else
        index, spellId, spellName = FindSpellListByName(spellInput)
    end

    if not index then
        print("|cff00ff00[Shingen]|r 未在 spellsList 中找到技能: " .. spellInput)
        return
    end

    self:SetInsertSpell(index, spellName, spellId)
end

function Shingen:InsertItemCommand(rest)
    rest = strtrim(rest or "")
    if rest == "" then
        print("|cff00ff00[Shingen]|r 用法: /sh t 物品名称或itemId")
        return
    end

    local itemInput = rest

    local index, itemId, itemName
    if itemInput:match("^%d+$") then
        local requestedItemId = tonumber(itemInput)
        if requestedItemId and requestedItemId > 0 then
            index, itemId, itemName = FindItemListById(requestedItemId)
        end
    else
        index, itemId, itemName = FindItemListByName(itemInput)
    end

    if not index then
        print("|cff00ff00[Shingen]|r 未在 itemsList 中找到物品: " .. itemInput)
        return
    end

    self:SetInsertItem(index, itemName, itemId)
end

-- 主爆发与次要爆发共用写入入口，命令和快捷条点击保持同样的计时与像素语义。
function Shingen:SetBurstTime(seconds, minor, silent)
    local c = self:GetCharConfig()
    if not c then return end
    local deadline = minor and "MinorBurstTime" or "BurstTime"
    local key = minor and "minorCooldowns" or "cooldowns"
    local blockName = minor and "次要爆发" or "爆发开关"
    local label = minor and "次要爆发" or "爆发"
    self[deadline] = GetTime() + (seconds > 0 and seconds or -1)
    c[key] = seconds > 0 and 1 or 0
    if not silent then
        if seconds > 0 then
            print("|cff00ff00[Shingen]|r " .. label .. "已开启（" .. seconds .. " 秒）")
        else
            print("|cff00ff00[Shingen]|r " .. label .. "已关闭")
        end
    end
    if self.UpdateBareStateBlock then
        self:UpdateBareStateBlock(blockName, { "配置开关", "状态" })
    end
    if self.RefreshQuickToggleAppearance then
        self:RefreshQuickToggleAppearance()
    end
end

-- 两种爆发均支持：省略参数 15 秒、on 3600 秒、off 或非正数关闭。
function Shingen:HandleBurstCommand(command)
    local name, arg = command:match("^(%S+)%s*(.*)$")
    if name ~= "cd" and name ~= "mcd" then return false end
    local seconds
    if arg == "" then
        seconds = 15
    elseif arg == "on" then
        seconds = 3600
    elseif arg == "off" then
        seconds = 0
    else
        seconds = tonumber(arg)
    end
    if not seconds or seconds ~= seconds or math.abs(seconds) == math.huge then
        print("|cff00ff00[Shingen]|r 无效秒数；用法: /sh " .. name .. " [on|off|秒数]")
    else
        self:SetBurstTime(seconds, name == "mcd")
    end
    return true
end

function Shingen:SlashCommand(input, editbox)
    input = strtrim(input or "")
    local command = string.lower(input)

    local c = self:GetCharConfig()
    if self:HandleBurstCommand(command) then
        return
    elseif command == "custom" or command:match("^custom%s+") then
        if not c then return end
        local value = tonumber(command:match("^custom%s+(.+)$") or "")
        if not value or value < 0 or value > 255 or value % 1 ~= 0 then
            print("|cff00ff00[Shingen]|r 用法: /sh custom x（x 为 0-255 的整数）")
            return
        end
        c.customMode = value
        if self.UpdateBareStateBlock then
            self:UpdateBareStateBlock("自定义模式", { "配置开关", "状态" })
        end
        print("|cff00ff00[Shingen]|r 自定义模式已设为 " .. value .. " / 255")
    elseif command == "potion" then
        if not c then return end
        c.potion = (c.potion == 0) and 1 or 0
        self:SwitchPotion()
    elseif command == "potion on" then
        if not c then return end
        c.potion = 1
        self:SwitchPotion()
    elseif command == "potion off" then
        if not c then return end
        c.potion = 0
        self:SwitchPotion()
    elseif command == "timer" then
        self:ToggleTimer()
    elseif command == "timer on" then
        self:StartTimer()
    elseif command == "timer off" then
        self:StopTimer()
    elseif command:match("^timer") then
        print("|cff00ff00[Shingen]|r 用法: /sh timer [on|off]")
    elseif command == "loop" then
        self:ToggleLoopTimer()
    elseif command == "loop on" then
        self:StartLoopTimer(255)
    elseif command == "loop off" then
        self:StopLoopTimer()
    elseif command:match("^loop%s+") then
        local secStr = command:match("^loop%s+(.+)$")
        local sec = tonumber(strtrim(secStr or ""))
        if not sec then
            print("|cff00ff00[Shingen]|r 无效秒数；请输入 1-255（例如 /sh loop 5），或使用 /sh loop on / /sh loop off。")
        elseif sec > 0 then
            self:StartLoopTimer(sec)
        else
            self:StopLoopTimer()
        end
    elseif command:match("^loop") then
        print("|cff00ff00[Shingen]|r 用法: /sh loop [on|off|秒数]")
    elseif command == "hide" then
        self:HideQuickToggleButton()
    elseif command == "show" then
        self:ShowQuickToggleButton()
    elseif command:match("^delay") then
        if not c then return end
        local secStr = command:match("^delay%s+(.+)$")
        local sec = 1
        if secStr then
            local trimmed = strtrim(secStr)
            if trimmed ~= "" then
                local parsed = tonumber(trimmed)
                if parsed and parsed > 0 then
                    sec = parsed
                else
                    print("|cff00ff00[Shingen]|r 无效秒数；请输入正数（例如 /sh delay 5），或不写秒数使用默认 1 秒。")
                    return
                end
            end
        end
        local delayAlreadyActive = fuDelayEndTimer ~= nil
        if fuDelayEndTimer then
            fuDelayEndTimer:Cancel()
            fuDelayEndTimer = nil
        end
        c.delay = 1
        self:SwitchDelay()
        fuDelayEndTimer = C_Timer.NewTimer(sec, function()
            fuDelayEndTimer = nil
            local cc = Shingen:GetCharConfig()
            if cc then
                cc.delay = 0
                print("延迟已恢复。")
                Shingen:SwitchDelay()
            end
        end)
        if not delayAlreadyActive then
            print("延迟已生效，" .. sec .. " 秒后恢复。")
        end
    elseif command == "i" or command:match("^i%s+") then
        -- 从原始 input 解析技能名或 spellId（保留中文技能名）
        local rest = input:match("^[iI]%s+(.*)$") or ""
        self:InsertSpellCommand(rest)
    elseif command == "t" or command:match("^t%s+") then
        -- 从原始 input 解析物品名或 itemId（保留中文物品名）
        local rest = input:match("^[tT]%s+(.*)$") or ""
        self:InsertItemCommand(rest)
    elseif command == "help" or command == "" then
        print("|cff00ff00Shingen|r 命令列表:")
        print("爆发开关（开启 15 秒）: /sh cd")
        print("|cff00ff00开启|r爆发（长计时 3600 秒）: /sh cd on")
        print("|cffff0000关闭|r爆发: /sh cd off")
        print("按秒开启爆发: /sh cd xx（xx 为秒数，<=0 等同关闭）")
        print("次要爆发（开启 15 秒）: /sh mcd")
        print("开启次要爆发（3600 秒）: /sh mcd on")
        print("关闭次要爆发: /sh mcd off")
        print("按秒开启次要爆发: /sh mcd xx（<=0 等同关闭）")
        print("自定义模式（像素值 x / 255）: /sh custom x（0-255 整数）")
        print("爆发药水开关: /sh potion")
        print("|cff00ff00开启|r药水: /sh potion on")
        print("|cffff0000关闭|r药水: /sh potion off")
        print("切换计时器: /sh timer")
        print("|cff00ff00开启|r计时器: /sh timer on")
        print("|cffff0000关闭|r计时器: /sh timer off")
        print("切换循环计时器（1-255 秒）: /sh loop")
        print("|cff00ff00开启|r循环计时器（1-255 秒）: /sh loop on")
        print("|cffff0000关闭|r循环计时器: /sh loop off")
        print("按秒开启循环计时器: /sh loop xx（xx 为 1-255，<=0 等同关闭）")
        print("隐藏快捷控件: /sh hide")
        print("显示快捷控件: /sh show")
        print("临时 delay 标志（db.char.delay 置 1 持续 x 秒后归零）: /sh delay [秒]，省略秒数则为 1 秒")
        print("插入法术: /sh i 技能名称或spellId")
        print("插入物品: /sh t 物品名称或itemId")
        print("帮助: /sh help")
    else
        print("输入 /sh help 查看命令。")
    end
end
