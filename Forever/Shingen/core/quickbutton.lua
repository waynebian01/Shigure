--[[
快捷控件：上层显示爆发剩余时间，下层为药水开关与计时器两个按钮。
主爆发为红色；次要爆发启用时优先显示绿色及其剩余时间，结束后恢复主爆发。
两种爆发独立计时，进度均按 15 秒封顶；隐藏面板后仍由常驻驱动更新到期状态。
计时条左键开启主爆发 15 秒、右键关闭主爆发、中键开启 3600 秒；拖动保存位置。
次要爆发通过 /sh mcd [on|off|秒数] 控制，命令和点击共用 SetBurstTime。
--]]

local addon, ns = ... -- 保持 Shingen 文件惯例，本文件不引用 addon/ns

-- 固定 UI 像素尺寸常量（文件头集中定义，所有尺寸直接引用，不做缩放换算）
local PANEL_WIDTH = 180   -- 面板总宽（固定 UI 像素；两枚等宽按钮）
local ROW_HEIGHT = 16     -- 单行高度：计时条行与按钮行各占一行
local BAR_HEIGHT = 10     -- 计时条高度（严格垂直居中于 ROW_HEIGHT 行内）
local SPACING = 2         -- 计时条与层边、两行之间、按钮之间的间距
local PANEL_BORDER = 1    -- 面板外边框宽度
local BUTTON_BORDER = 1   -- 按钮外边框宽度
local CLICK_THRESHOLD = 5 -- 点击判定阈值：抬起时位移小于该像素数视为点击（按按键分派）
local FONT_SIZE = 12      -- 按钮文字字号

-- 配色表（照 panel.lua 蓝本全套，全部 CreateColor 创建）
local Black = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)              -- 纯黑（蓝本配色，本文件未引用）
local WindowBg = CreateColor(30 / 255, 30 / 255, 30 / 255, 1)        -- 面板内缩填充底色
local WindowText = CreateColor(0 / 255, 0 / 255, 0 / 255, 1)         -- 窗口文字黑（蓝本配色，本文件未引用）
local WindowBorder = CreateColor(83 / 255, 88 / 255, 91 / 255, 1)    -- 面板外边框灰
local Base = CreateColor(255 / 255, 255 / 255, 255 / 255, 1)         -- 基础白（蓝本配色，本文件未引用）
local ButtonBorder = CreateColor(52 / 255, 52 / 255, 52 / 255, 1)    -- 按钮边框灰
local ButtonHighlight = CreateColor(86 / 255, 86 / 255, 86 / 255, 1) -- 按钮悬停时边框亮灰
local ButtonMouseUp = CreateColor(43 / 255, 43 / 255, 43 / 255, 1)   -- 按钮填充常态灰
local ButtonMouseDown = CreateColor(37 / 255, 37 / 255, 37 / 255, 1) -- 按钮按下时填充暗灰
local SliderLeft = CreateColor(255 / 255, 79 / 255, 79 / 255, 1)     -- 主爆发计时条填充红
local RowHover = CreateColor(50 / 255, 50 / 255, 50 / 255, 1)        -- 行悬停灰（蓝本配色，本文件未引用）
local Text = CreateColor(230 / 255, 230 / 255, 230 / 255, 1)         -- 常规文字浅灰（蓝本配色，本文件未引用）
local DropdownBg = CreateColor(34 / 255, 34 / 255, 34 / 255, 1)      -- 下拉框底色（蓝本配色，本文件未引用）
local StateGreen = CreateColor(0.30, 0.75, 0.40, 1)                  -- 状态绿：按钮"开启/默认"态文字色
local StateYellow = CreateColor(0.85, 0.75, 0.30, 1)                 -- 状态黄：按钮"爆发药开启"态文字色
local StateBlue = CreateColor(0.41, 0.80, 0.94, 1)                   -- 状态蓝：按钮"官方"态文字色

-- 命名空间保存两种爆发的截止时间（GetTime 纪元），初始 0 = 未开启；
-- 旧档开关状态不迁移，重启归零，首帧静默纠正。
Shingen = Shingen or {}
Shingen.BurstTime = 0
Shingen.MinorBurstTime = 0

-- 常驻驱动与面板可见性解耦，仅在两种爆发状态翻转时写配置并刷新像素。
local driverFrame = CreateFrame("Frame")
local burstStates = {
    { deadline = "BurstTime", key = "cooldowns", blockName = "爆发开关" },
    { deadline = "MinorBurstTime", key = "minorCooldowns", blockName = "次要爆发" },
}
driverFrame:SetScript("OnUpdate", function()
    local c = Shingen:GetCharConfig()
    if not c then return end
    local now = GetTime()
    for _, burst in ipairs(burstStates) do
        local active = Shingen[burst.deadline] > now and 1 or 0
        if (c[burst.key] or 0) ~= active then
            c[burst.key] = active
            if Shingen.UpdateBareStateBlock then
                Shingen:UpdateBareStateBlock(burst.blockName, { "配置开关", "状态" })
            end
        end
    end
end)

function Shingen:UpdateQuickToggleVisibility()
    local f = self.quickToggleFrame
    if not f then return end
    local c = self:GetCharConfig()
    local show = not c or (c.quickButtonShow ~= false)
    self:RefreshQuickToggleAppearance()
    if show then
        f:Show()
    else
        f:Hide()
    end
end

function Shingen:HideQuickToggleButton()
    local c = self:GetCharConfig()
    if not c then return end
    c.quickButtonShow = false
    self:UpdateQuickToggleVisibility()
    print("|cff00ff00[Shingen]|r 快捷控件已隐藏。")
end

function Shingen:ShowQuickToggleButton()
    local c = self:GetCharConfig()
    if not c then return end
    c.quickButtonShow = true
    self:UpdateQuickToggleVisibility()
    print("|cff00ff00[Shingen]|r 快捷控件已显示。")
end

-- 计时器按钮显示态：未运行显示「计时器」，运行中显示已过秒数（与 StartTimer/StopTimer 共用）
function Shingen:RefreshTimerAppearance()
    local f = self.quickToggleFrame
    local label = f and f.timerButton and f.timerButton.label
    if not label then return end
    if self.IsTimerRunning and self:IsTimerRunning() then
        label:SetText(tostring(self:GetTimerElapsed() or 0))
        label:SetTextColor(StateYellow:GetRGB())
    else
        label:SetText("计时器")
        label:SetTextColor(StateGreen:GetRGB())
    end
end

-- 切换按钮显示态的唯一刷新入口：按 GetCharConfig() 配置派生（命令改配置与按钮点击共用单一数据源）
function Shingen:RefreshQuickToggleAppearance()
    local f = self.quickToggleFrame
    if not f or not f.buttons then return end
    local c = self:GetCharConfig()
    if c then
        for _, button in ipairs(f.buttons) do
            local def = button.def
            if def.isOn(c) then
                button.label:SetText(def.onText)
                button.label:SetTextColor(def.onColor:GetRGB())
            else
                button.label:SetText(def.offText)
                button.label:SetTextColor(def.offColor:GetRGB())
            end
        end
    end
    self:RefreshTimerAppearance()
end

local function SaveQuickButtonPosition(self)
    local c = Shingen:GetCharConfig()
    if not c then return end
    local p, _, rp, x, y = self:GetPoint(1)
    if p and x and y then
        c.quickButtonPoint = p
        c.quickButtonRelPoint = rp or p
        c.quickButtonX = x
        c.quickButtonY = y
    end
end

function Shingen:InitQuickToggleButton()
    if self.quickToggleFrame then
        self:UpdateQuickToggleVisibility()
        return
    end

    local c = Shingen:GetCharConfig()
    -- 面板根框体：匿名（全局命名 ShingenBurstPanel 不再创建），可拖动、位置记忆
    local f = CreateFrame("Frame", nil, UIParent)
    f:SetSize(PANEL_WIDTH, 2 * PANEL_BORDER + SPACING + 2 * ROW_HEIGHT) -- 总高 = 上边框 1 + 计时条行 16 + 间距 2 + 按钮行 16 + 下边框 1 = 36
    f:SetFrameStrata("MEDIUM")
    f:SetMovable(true)
    f:SetClampedToScreen(true)
    f:EnableMouse(true)
    f:RegisterForDrag("LeftButton")

    -- 位置：拖动存档优先（quickButtonPoint/RelPoint/X/Y），否则默认 CENTER + quickButtonCX/quickButtonCY
    local p = c and c.quickButtonPoint
    if p and c.quickButtonX and c.quickButtonY then
        f:SetPoint(p, UIParent, c.quickButtonRelPoint or p, c.quickButtonX, c.quickButtonY)
    else
        f:SetPoint("CENTER", UIParent, "CENTER", c and c.quickButtonCX or 180, c and c.quickButtonCY or -100)
    end

    -- 面板外观：BACKGROUND 铺满设边框色，ARTWORK 内缩 PANEL_BORDER 设填充色（照蓝本）
    local panelBg = f:CreateTexture(nil, "BACKGROUND") -- 面板边框层：铺满面板设外边框灰
    panelBg:SetAllPoints()
    panelBg:SetColorTexture(WindowBorder:GetRGB())
    local panelArt = f:CreateTexture(nil, "ARTWORK") -- 面板填充层：内缩 PANEL_BORDER 设底色
    panelArt:SetPoint("TOPLEFT", f, "TOPLEFT", PANEL_BORDER, -PANEL_BORDER)
    panelArt:SetPoint("BOTTOMRIGHT", f, "BOTTOMRIGHT", -PANEL_BORDER, PANEL_BORDER)
    panelArt:SetColorTexture(WindowBg:GetRGB())
    -- 面板根拖动脚本（照蓝本）：OnDragStart 立即移动，OnDragStop 停止并清除用户放置标记
    f:SetScript("OnDragStart", function(self)
        self:StartMoving()
    end)
    f:SetScript("OnDragStop", function(self)
        self:StopMovingOrSizing()
        SaveQuickButtonPosition(self) -- 面板根拖动结束：保存位置存档（与 barLayer 拖动分支同构）
        self:SetUserPlaced(false) -- 清除用户放置标记，避免位置被客户端保存
    end)

    -- 爆发计时条层：高 ROW_HEIGHT 内缩边框，可点击按按键分派（左键 +15 秒/右键取消/中键 +3600 秒），也参与面板拖动
    local pressX, pressY = 0, 0 -- 计时条按下时记录的光标位置，用于抬起时判定点击还是拖动

    local barLayer = CreateFrame("Frame", nil, f) -- 计时条层：高 ROW_HEIGHT，内缩 PANEL_BORDER，负责点击分派与拖动
    barLayer:SetPoint("TOPLEFT", f, "TOPLEFT", PANEL_BORDER, -PANEL_BORDER)
    barLayer:SetPoint("TOPRIGHT", f, "TOPRIGHT", -PANEL_BORDER, -PANEL_BORDER)
    barLayer:SetHeight(ROW_HEIGHT)
    barLayer:EnableMouse(true)

    -- 悬停 Tooltip：标题行 SetText 绿（Shingen）、按键说明 AddLine 灰，
    -- 锚定层右侧 SPACING 偏移，层级 TOOLTIP；OnMouseDown 隐藏、点击分派后由 OnMouseUp 恢复
    local function ShowBurstTooltip()
        GameTooltip:SetOwner(barLayer, "ANCHOR_RIGHT", SPACING, 0)
        GameTooltip:SetFrameStrata("TOOLTIP")
        GameTooltip:SetFrameLevel(1000)
        GameTooltip:SetText("Shingen", 0, 1, 0.6, 1, true)
        GameTooltip:AddLine("左键：爆发15秒", 0.8, 0.8, 0.8, true)
        GameTooltip:AddLine("右键：取消爆发", 0.8, 0.8, 0.8, true)
        GameTooltip:AddLine("中键：永久爆发", 0.8, 0.8, 0.8, true)
        GameTooltip:AddLine("绿色：次要爆发（/sh mcd）", 0.8, 0.8, 0.8, true)
        GameTooltip:Show()
    end
    barLayer:SetScript("OnEnter", ShowBurstTooltip) -- 悬停进入显示提示
    barLayer:SetScript("OnLeave", function()        -- 光标移出层后隐藏提示
        GameTooltip:Hide()
    end)

    -- 计时条：轨道全透明、主爆发红色 / 次要爆发绿色，条高 BAR_HEIGHT 在层内严格垂直居中，条宽 = 层宽 - 2*SPACING
    local track = barLayer:CreateTexture(nil, "BACKGROUND") -- 计时条轨道（全透明，仅提供宽度/高度布局基准）
    track:SetPoint("TOPLEFT", barLayer, "TOPLEFT", SPACING, -(ROW_HEIGHT - BAR_HEIGHT) / 2)
    track:SetPoint("TOPRIGHT", barLayer, "TOPRIGHT", -SPACING, -(ROW_HEIGHT - BAR_HEIGHT) / 2)
    track:SetHeight(BAR_HEIGHT)
    track:SetColorTexture(0, 0, 0, 0)

    local fill = barLayer:CreateTexture(nil, "ARTWORK") -- 计时条填充：锚在 track 左右底边，高度随 track（BAR_HEIGHT）
    fill:SetPoint("TOPLEFT", track, "TOPLEFT", 0, 0)
    fill:SetPoint("BOTTOMLEFT", track, "BOTTOMLEFT", 0, 0)
    fill:SetColorTexture(SliderLeft:GetRGB())

    -- barLayer 的 OnUpdate 只做显示：优先取次要爆发，剩余时间封顶 15 秒后按比例收缩；
    -- 归 0（含初始 BurstTime=0）或轨道宽无效时隐藏填充——SetWidth(0) 会清除 desired width
    -- 导致 1px 残段，故归零改 Hide 而不是设宽 0；c.cooldowns 的派生在独立 driver frame
    barLayer:SetScript("OnUpdate", function(self)
        local now = GetTime()
        -- 次要爆发启用时优先显示绿色及其剩余时间，到期后恢复主爆发进度。
        local minor = Shingen.MinorBurstTime > now
        local remaining = (minor and Shingen.MinorBurstTime or Shingen.BurstTime) - now
        fill:SetColorTexture((minor and StateGreen or SliderLeft):GetRGB())
        if remaining > 15 then
            remaining = 15
        elseif remaining < 0 then
            remaining = 0
        end
        local trackWidth = self:GetWidth() - 2 * SPACING
        if trackWidth > 0 and remaining > 0 then
            fill:Show()
            fill:SetWidth(trackWidth * remaining / 15)
        else
            fill:Hide()
        end
    end)

    -- 按下记录光标位置并启动面板拖动；抬起时位移小于 CLICK_THRESHOLD 像素判定为点击，按按键分派（见 OnMouseUp）
    barLayer:SetScript("OnMouseDown", function()
        GameTooltip:Hide() -- 按下瞬间隐藏 Tooltip，拖动过程不显示
        pressX, pressY = GetCursorPosition()
        f:StartMoving()
    end)
    -- 抬起时位移小于 CLICK_THRESHOLD 像素判定为点击：左键 +15 秒 / 右键取消（now-1）/
    -- 中键 +3600 秒长计时 / 其余按键维持 +15，写 Shingen.BurstTime 并同步写 c.cooldowns（静默）；
    -- 位移大则视为拖动结束并保存位置存档（点击不保存位置）；恒停止移动并清除用户放置标记
    barLayer:SetScript("OnMouseUp", function(self, button)
        local x, y = GetCursorPosition()
        if math.abs(x - pressX) < CLICK_THRESHOLD and math.abs(y - pressY) < CLICK_THRESHOLD then
            local seconds = button == "RightButton" and 0 or (button == "MiddleButton" and 3600 or 15)
            Shingen:SetBurstTime(seconds, false, true)
            ShowBurstTooltip() -- 判定为点击并分派后立即恢复 Tooltip（光标仍在层内）；拖动不恢复
        else
            SaveQuickButtonPosition(f) -- 拖动结束：保存位置存档（点击不保存位置）
        end
        f:StopMovingOrSizing()
        f:SetUserPlaced(false) -- 清除用户放置标记，避免位置被客户端保存
    end)
    -- 计时条层结束

    -- 两个等宽按钮：不喝药/爆发药、计时器
    local buttonRow = CreateFrame("Frame", nil, f) -- 按钮行层：位于计时条层下方 SPACING 处，高 ROW_HEIGHT
    buttonRow:SetPoint("TOPLEFT", barLayer, "BOTTOMLEFT", 0, -SPACING)
    buttonRow:SetPoint("TOPRIGHT", barLayer, "BOTTOMRIGHT", 0, -SPACING)
    buttonRow:SetHeight(ROW_HEIGHT)

    local contentWidth = PANEL_WIDTH - 2 * PANEL_BORDER  -- 面板内缩边框后的内容区宽度
    local buttonCount = 2
    local buttonWidth = (contentWidth - (buttonCount - 1) * SPACING) / buttonCount

    -- 字体：从 GameFontHighlightSmall 取字体文件/样式标志与阴影，只取一次供所有按钮使用
    local fontFile, _, fontFlags = GameFontHighlightSmall:GetFont()                    -- 字体文件路径与样式标志（居中位丢弃）
    local shadowR, shadowG, shadowB, shadowA = GameFontHighlightSmall:GetShadowColor() -- 字体阴影 RGBA
    local shadowOffX, shadowOffY = GameFontHighlightSmall:GetShadowOffset()            -- 字体阴影偏移

    -- 按钮定义：off/on 态文字（字间半角空格）与颜色、按配置判定的显示态与点击翻转动作。
    -- 显示态完全由 RefreshQuickToggleAppearance 按 c 派生（无 local isOn 双轨漂移）
    local buttonDefs = {
        {
            offText = "不喝药", offColor = StateYellow,
            onText = "爆发药", onColor = StateGreen,
            isOn = function(c) return (c.potion or 0) == 1 end,
            applyClick = function(c)
                c.potion = (c.potion == 0) and 1 or 0
                if Shingen.SwitchPotion then Shingen:SwitchPotion() end
            end,
        },
    }

    local prevButton -- 上一枚创建的按钮：用于把后续按钮依次锚在其右侧
    local buttons = {} -- 药水按钮数组，RefreshQuickToggleAppearance 逐项刷新显示态
    for i, def in ipairs(buttonDefs) do
        local button = CreateFrame("Button", nil, buttonRow)

        -- PhantomProject 按钮手法：BUTTON_BORDER=1 边框 + 内缩填充
        local bg = button:CreateTexture(nil, "BACKGROUND")
        bg:SetAllPoints()
        bg:SetColorTexture(ButtonBorder:GetRGB())

        local art = button:CreateTexture(nil, "ARTWORK")
        art:SetPoint("TOPLEFT", button, "TOPLEFT", BUTTON_BORDER, -BUTTON_BORDER)
        art:SetPoint("BOTTOMRIGHT", button, "BOTTOMRIGHT", -BUTTON_BORDER, BUTTON_BORDER)
        art:SetColorTexture(ButtonMouseUp:GetRGB())

        -- 文字：沿用 GameFontHighlightSmall 字体文件/样式标志与阴影，固定 FONT_SIZE 字号，
        -- 文字与颜色由 RefreshQuickToggleAppearance 统一按配置设置（默认白色必须显式改色）
        local label = button:CreateFontString(nil, "OVERLAY")
        label:SetFont(fontFile or "Fonts\\FRIZQT__.TTF", FONT_SIZE, fontFlags)
        label:SetShadowColor(shadowR, shadowG, shadowB, shadowA)
        label:SetShadowOffset(shadowOffX, shadowOffY)
        label:SetJustifyH("CENTER")
        label:SetJustifyV("MIDDLE")
        label:SetPoint("CENTER")

        button.def = def
        button.label = label
        -- 点击 = 翻转配置属性 + SwitchXxx（打印提示 + 刷新 stateblock 像素 + 刷新按钮显示态）
        button:SetScript("OnClick", function(self)
            local cc = Shingen:GetCharConfig()
            if not cc then return end
            self.def.applyClick(cc)
        end)

        -- 悬停边框变亮、按下填充变暗（PhantomProject 反馈）
        button:SetScript("OnEnter", function()
            bg:SetColorTexture(ButtonHighlight:GetRGB())
        end)
        button:SetScript("OnLeave", function()
            bg:SetColorTexture(ButtonBorder:GetRGB())
        end)
        button:SetScript("OnMouseDown", function()
            art:SetColorTexture(ButtonMouseDown:GetRGB())
        end)
        button:SetScript("OnMouseUp", function()
            art:SetColorTexture(ButtonMouseUp:GetRGB())
        end)

        -- 布局：等宽铺满，按钮间距 SPACING
        button:SetSize(buttonWidth, ROW_HEIGHT)
        if prevButton then
            button:SetPoint("TOPLEFT", prevButton, "TOPRIGHT", SPACING, 0)
        else
            button:SetPoint("TOPLEFT", buttonRow, "TOPLEFT", 0, 0)
        end
        prevButton = button
        buttons[i] = button
    end
    f.buttons = buttons

    -- 计时器按钮：默认「计时器」，左击开始 / 右击关闭，运行中显示已过秒数
    local timerButton = CreateFrame("Button", nil, buttonRow)

    local timerBg = timerButton:CreateTexture(nil, "BACKGROUND")
    timerBg:SetAllPoints()
    timerBg:SetColorTexture(ButtonBorder:GetRGB())

    local timerArt = timerButton:CreateTexture(nil, "ARTWORK")
    timerArt:SetPoint("TOPLEFT", timerButton, "TOPLEFT", BUTTON_BORDER, -BUTTON_BORDER)
    timerArt:SetPoint("BOTTOMRIGHT", timerButton, "BOTTOMRIGHT", -BUTTON_BORDER, BUTTON_BORDER)
    timerArt:SetColorTexture(ButtonMouseUp:GetRGB())

    local timerLabel = timerButton:CreateFontString(nil, "OVERLAY")
    timerLabel:SetFont(fontFile or "Fonts\\FRIZQT__.TTF", FONT_SIZE, fontFlags)
    timerLabel:SetShadowColor(shadowR, shadowG, shadowB, shadowA)
    timerLabel:SetShadowOffset(shadowOffX, shadowOffY)
    timerLabel:SetJustifyH("CENTER")
    timerLabel:SetJustifyV("MIDDLE")
    timerLabel:SetPoint("CENTER")

    local function ShowTimerTooltip()
        GameTooltip:SetOwner(timerButton, "ANCHOR_RIGHT", SPACING, 0)
        GameTooltip:SetFrameStrata("TOOLTIP")
        GameTooltip:SetFrameLevel(1000)
        GameTooltip:SetText("计时器", 0, 1, 0.6, 1, true)
        GameTooltip:AddLine("左键：开始计时", 0.8, 0.8, 0.8, true)
        GameTooltip:AddLine("右键：关闭计时器", 0.8, 0.8, 0.8, true)
        GameTooltip:Show()
    end

    timerButton:SetScript("OnEnter", function()
        timerBg:SetColorTexture(ButtonHighlight:GetRGB())
        ShowTimerTooltip()
    end)
    timerButton:SetScript("OnLeave", function()
        timerBg:SetColorTexture(ButtonBorder:GetRGB())
        GameTooltip:Hide()
    end)
    timerButton:SetScript("OnMouseDown", function()
        timerArt:SetColorTexture(ButtonMouseDown:GetRGB())
    end)
    timerButton:SetScript("OnMouseUp", function()
        timerArt:SetColorTexture(ButtonMouseUp:GetRGB())
    end)

    timerButton:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    timerButton:SetScript("OnClick", function(_, mouseButton)
        if mouseButton == "LeftButton" then
            if Shingen.StartTimer then Shingen:StartTimer() end
        elseif mouseButton == "RightButton" then
            if Shingen.StopTimer then Shingen:StopTimer() end
        end
    end)

    timerButton:SetSize(buttonWidth, ROW_HEIGHT)
    if prevButton then
        timerButton:SetPoint("TOPLEFT", prevButton, "TOPRIGHT", SPACING, 0)
    else
        timerButton:SetPoint("TOPLEFT", buttonRow, "TOPLEFT", 0, 0)
    end
    timerButton.label = timerLabel
    f.timerButton = timerButton
    -- 按钮行结束

    self.quickToggleFrame = f
    self:UpdateQuickToggleVisibility()
end
