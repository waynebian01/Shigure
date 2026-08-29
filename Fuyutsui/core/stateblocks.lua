local addon, ns = ...

local EvaluateColorFromBoolean = C_CurveUtil.EvaluateColorFromBoolean

local state = Fuyutsui.state
local target = Fuyutsui.target
local focus = Fuyutsui.focus
local mouseover = Fuyutsui.mouseover
local pet = Fuyutsui.pet
local boss = Fuyutsui.boss

local ColorValue0 = CreateColor(0, 0, 0, 1)
local ColorValue1 = CreateColor(0, 0, 1 / 255, 1)

Fuyutsui.powerNameMap = {
    ["MANA"] = "法力值",
    ["RAGE"] = "怒气值",
    ["FOCUS"] = "集中值",
    ["ENERGY"] = "能量值",
    ["RUNES"] = "符文",
    ["RUNIC_POWER"] = "符文能量",
    ["LUNAR_POWER"] = "星界能量",
    ["MAELSTROM"] = "漩涡值",
    ["INSANITY"] = "狂乱值",
    ["ARCANE_CHARGES"] = "奥术充能",
    ["FURY"] = "恶魔之怒",
    ["PAIN"] = "痛苦值",
    ["COMBO_POINTS"] = "连击点",
    ["HOLY_POWER"] = "神圣能量",
    ["ESSENCE"] = "精华能量",
    ["SOUL_SHARDS"] = "灵魂碎片",
    ["CHI"] = "真气",
}

local function GetItemCooldownPixel(self, countKey, itemID)
    if not self.state[countKey] then
        self:GetItemCount()
    end
    local remainingTime = self:GetItemRemainingTime(itemID)
    if remainingTime and self.state[countKey] > 0 then
        return math.min(1, remainingTime / 255)
    end
    return 1
end

--- mode: "cast" | "castElapsed" | "channel"
function Fuyutsui:GetUnitCastPixel(unit, mode)
    local castCurve = self.castCurve
    if mode == "channel" then
        local channel = UnitChannelDuration(unit)
        if not channel then return 0 end
        local _, _, _, _, _, _, _, _, _, _, castBarID = UnitChannelInfo(unit)
        if not castBarID then return nil end
        local channelDurationColor = channel:EvaluateRemainingDuration(castCurve)
        ---@diagnostic disable-next-line: param-type-mismatch
        local _, _, b = channelDurationColor:GetRGB()
        return b
    end

    local cast = UnitCastingDuration(unit)
    if not cast then return 0 end
    local castingDurationColor
    if mode == "castElapsed" then
        castingDurationColor = cast:EvaluateElapsedDuration(castCurve)
    else
        castingDurationColor = cast:EvaluateRemainingDuration(castCurve)
    end
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = castingDurationColor:GetRGB()
    return b
end

--- mode: "cast" | "channel"
function Fuyutsui:GetUnitInterruptiblePixel(unit, mode)
    if mode == "channel" then
        local channel = UnitChannelDuration(unit)
        if not channel then return 0 end
        local _, _, _, _, _, _, notInterruptible, _, _, _, castBarID = UnitChannelInfo(unit)
        if not castBarID then return nil end
        ---@diagnostic disable-next-line: param-type-mismatch
        local interruptibleColor = EvaluateColorFromBoolean(notInterruptible, ColorValue0, ColorValue1)
        local _, _, interruptible = interruptibleColor:GetRGB()
        return interruptible
    end

    if not UnitCastingDuration(unit) then return 0 end
    local _, _, _, _, _, _, _, notInterruptible = UnitCastingInfo(unit)
    ---@diagnostic disable-next-line: param-type-mismatch
    local interruptibleColor = EvaluateColorFromBoolean(notInterruptible, ColorValue0, ColorValue1)
    local _, _, interruptible = interruptibleColor:GetRGB()
    return interruptible
end

-- blocks.state 键：下列分类用名称本身；单位分类用 分类..名称
local bareKeyCategories = {
    ["状态"] = true,
    ["能量"] = true,
    ["物品"] = true,
    ["配置开关"] = true,
}

local function GetRunePixel()
    if state.runeCount ~= nil then return state.runeCount end
    return (state.power and state.power["RUNES"]) or 0
end

local function GetConfigPixel(self, key)
    local c = self.db and self.db.char
    return c and (c[key] / 255) or 0
end

-- stateBlockGetters[分类][名称]
local stateBlockGetters = {
    ["状态"] = {
        ["职业"] = function(self) return self.state.classId / 255 end,
        ["专精"] = function(self) return self.state.specIndex / 255 end,
        ["有效性"] = function() return state.valid or 0 end,
        ["战斗时间"] = function() return state.combatTime or 0 end,
        ["移动"] = function() return state.moving or 0 end,
        ["生命值"] = function() return state.healthPercent or 0 end,
        ["一键辅助"] = function() return state.assistantSpell or 0 end,
        ["插入法术"] = function() return state.insertSpell or 0 end,
        ["队伍类型"] = function() return state.groupType or 0 end,
        ["队伍人数"] = function() return state.groupCount or 0 end,
        ["首领战"] = function() return state.bossID or 0 end,
        ["难度"] = function() return (state.difficultyID or 0) / 255 end,
        ["英雄天赋"] = function(self) return (self.state.heroTalent or 0) / 255 end,
        ["施法目标"] = function() return state.castTargetIndex or 0 end,
        ["施法技能"] = function() return state.castingSpell or 0 end,
        ["敌人数量"] = function() return state.enemyCount or 0 end,
        ["敌人数-无仇恨"] = function() return state.noThreatEnemyCount or 0 end,
        ["敌人数-有仇恨"] = function() return state.threatEnemyCount or 0 end,
        ["酒池"] = function() return state.staggerPercent or 0 end,
        ["神圣军备"] = function() return state.holyArmaments or 0 end,
        ["吸血鬼打击"] = function() return state.VampiricStrike or 0 end,
        ["收割者战刃"] = function() return state.reaverGlaive or 0 end,
        ["英勇打击"] = function() return state.heroicStrike or 0 end,
        ["吸血鬼打击"] = function() return state.vampiricStrike or 0 end,
        -- 兼容：旧职业表仍把能量/配置/物品写在 ["状态"] 下
        ["符文"] = function() return GetRunePixel() end,
        ["姿态"] = function() return state.shapeshiftFormID or 0 end,
        ["天启骑士数量"] = function() return state.knightCount or 0 end,
        ["自律"] = function() return state.forbearance or 0 end,

        ["爆发开关"] = function(self) return GetConfigPixel(self, "cooldowns") end,
        ["AOE开关"] = function(self) return GetConfigPixel(self, "aoeMode") end,
        ["输出模式"] = function(self) return GetConfigPixel(self, "dpsMode") end,
        ["爆发药水开关"] = function(self) return GetConfigPixel(self, "potion") end,
        ["延迟"] = function(self) return GetConfigPixel(self, "delay") end,

        ["治疗药水"] = function(self) return GetItemCooldownPixel(self, "HealthPotionCount", 241304) end,
        ["魔法药水"] = function(self) return GetItemCooldownPixel(self, "ManaPotionCount", 241301) end,
        ["治疗石"] = function(self) return GetItemCooldownPixel(self, "HealthstoneCount", 5512) end,
        ["鲁莽药水"] = function(self) return GetItemCooldownPixel(self, "RecklessnessCount", 241288) end,
        ["圣光潜力"] = function(self) return GetItemCooldownPixel(self, "LightsPotentialCount", 241308) end,

        ["施法(正计时)"] = function(self) return self:GetUnitCastPixel("player", "castElapsed") end,
        ["施法(倒计时)"] = function(self) return self:GetUnitCastPixel("player", "cast") end,
        ["引导"] = function(self)
            if not state.channeling then
                state.channelingDuration = 0
                return 0
            end
            local channel = UnitChannelDuration("player")
            if channel then
                local channelDurationColor = channel:EvaluateRemainingDuration(self.castCurve)
                ---@diagnostic disable-next-line: param-type-mismatch
                local _, _, b = channelDurationColor:GetRGB()
                state.channelingDuration = b
                return b
            end
            state.channelingDuration = 0
            return 0
        end,
        ["蓄力"] = function(self)
            if not state.empowering then
                state.empowerDuration = 0
                return 0
            end
            local empowerDuration = UnitEmpoweredChannelDuration("player")
            if empowerDuration then
                local empowerDurationColor = empowerDuration:EvaluateRemainingDuration(self.castCurve)
                ---@diagnostic disable-next-line: param-type-mismatch
                local _, _, b = empowerDurationColor:GetRGB()
                state.empowerDuration = b
                return b
            end
            state.empowerDuration = 0
            return 0
        end,
        ["蓄力层数"] = function(self)
            if not state.empowering then
                state.empowerStage = 0
                return 0
            end
            local empowerStages = UnitEmpoweredStageDurations("player")
            if empowerStages then
                for k, v in pairs(empowerStages) do
                    local empower = v:EvaluateRemainingDuration(self.castCurve)
                    ---@diagnostic disable-next-line: param-type-mismatch
                    local _, _, b = empower:GetRGB()
                    state.empowerStage = (k - 1) / 255
                    if b > 0 then
                        break
                    end
                end
                return state.empowerStage or 0
            end
            state.empowerStage = 0
            return 0
        end,
    },
    ["能量"] = {
        ["符文"] = function() return GetRunePixel() end,
        ["增压层数"] = function() return state.chargedComboPoints or 0 end,
    },
    ["物品"] = {
        ["治疗药水"] = function(self) return GetItemCooldownPixel(self, "HealthPotionCount", 241304) end,
        ["魔法药水"] = function(self) return GetItemCooldownPixel(self, "ManaPotionCount", 241301) end,
        ["治疗石"] = function(self) return GetItemCooldownPixel(self, "HealthstoneCount", 5512) end,
        ["鲁莽药水"] = function(self) return GetItemCooldownPixel(self, "RecklessnessCount", 241288) end,
        ["圣光潜力"] = function(self) return GetItemCooldownPixel(self, "LightsPotentialCount", 241308) end,
    },
    ["配置开关"] = {
        ["爆发开关"] = function(self) return GetConfigPixel(self, "cooldowns") end,
        ["AOE开关"] = function(self) return GetConfigPixel(self, "aoeMode") end,
        ["输出模式"] = function(self) return GetConfigPixel(self, "dpsMode") end,
        ["爆发药水开关"] = function(self) return GetConfigPixel(self, "potion") end,
        ["延迟"] = function(self) return GetConfigPixel(self, "delay") end,
    },
    ["目标"] = {
        ["类型"] = function() return target.type or 0 end,
        ["驱散类型"] = function() return 0 end,
        ["生命值"] = function() return target.healthPercent or 0 end,
        ["距离"] = function()
            if not target.maxRange then return nil end
            return target.maxRange / 255
        end,
        ["施法(倒计时)"] = function(self) return self:GetUnitCastPixel("target", "cast") end,
        ["施法(正计时)"] = function(self) return self:GetUnitCastPixel("target", "castElapsed") end,
        ["施法可打断"] = function(self) return self:GetUnitInterruptiblePixel("target", "cast") end,
        ["引导"] = function(self) return self:GetUnitCastPixel("target", "channel") end,
        ["引导可打断"] = function(self) return self:GetUnitInterruptiblePixel("target", "channel") end,
    },
    ["焦点"] = {
        ["类型"] = function() return focus.type or 0 end,
        ["驱散类型"] = function() return 0 end,
        ["生命值"] = function() return focus.healthPercent or 0 end,
        ["距离"] = function()
            if not focus.maxRange then return nil end
            return focus.maxRange / 255
        end,
        ["施法(倒计时)"] = function(self) return self:GetUnitCastPixel("focus", "cast") end,
        ["施法(正计时)"] = function(self) return self:GetUnitCastPixel("focus", "castElapsed") end,
        ["施法可打断"] = function(self) return self:GetUnitInterruptiblePixel("focus", "cast") end,
        ["引导"] = function(self) return self:GetUnitCastPixel("focus", "channel") end,
        ["引导可打断"] = function(self) return self:GetUnitInterruptiblePixel("focus", "channel") end,
    },
    ["鼠标"] = {
        ["类型"] = function() return mouseover.type or 0 end,
        ["驱散类型"] = function() return 0 end,
        ["生命值"] = function() return mouseover.healthPercent or 0 end,
        ["距离"] = function()
            if not mouseover.maxRange then return nil end
            return mouseover.maxRange / 255
        end,
        ["施法(倒计时)"] = function(self) return self:GetUnitCastPixel("mouseover", "cast") end,
        ["施法(正计时)"] = function(self) return self:GetUnitCastPixel("mouseover", "castElapsed") end,
        ["施法可打断"] = function(self) return self:GetUnitInterruptiblePixel("mouseover", "cast") end,
        ["引导"] = function(self) return self:GetUnitCastPixel("mouseover", "channel") end,
        ["引导可打断"] = function(self) return self:GetUnitInterruptiblePixel("mouseover", "channel") end,
    },
    ["宠物"] = {
        ["存在"] = function() return pet.exists or 0 end,
        ["生命值"] = function() return pet.healthPercent or 0 end,
    },
}

for index = 1, 5 do
    local unit = "boss" .. index
    local category = "首领" .. index
    local cache = boss[unit]

    stateBlockGetters[category] = {
        ["类型"] = function() return cache.type or 0 end,
        ["驱散类型"] = function() return 0 end,
        ["生命值"] = function() return cache.healthPercent or 0 end,
        ["距离"] = function()
            if not cache.maxRange then return nil end
            return cache.maxRange / 255
        end,
        ["施法(倒计时)"] = function(self) return self:GetUnitCastPixel(unit, "cast") end,
        ["施法(正计时)"] = function(self) return self:GetUnitCastPixel(unit, "castElapsed") end,
        ["施法可打断"] = function(self) return self:GetUnitInterruptiblePixel(unit, "cast") end,
        ["引导"] = function(self) return self:GetUnitCastPixel(unit, "channel") end,
        ["引导可打断"] = function(self) return self:GetUnitInterruptiblePixel(unit, "channel") end,
    }
end

for powerType, powerName in pairs(Fuyutsui.powerNameMap) do
    if powerName ~= "符文" then
        local pt = powerType
        local getter = function()
            return (state.power and state.power[pt]) or 0
        end
        if not stateBlockGetters["状态"][powerName] then
            stateBlockGetters["状态"][powerName] = getter
        end
        if not stateBlockGetters["能量"][powerName] then
            stateBlockGetters["能量"][powerName] = getter
        end
    end
end

-- UpdateStateBlock("状态", "职业") / UpdateStateBlock("能量", "符文") / UpdateStateBlock("目标", "生命值")
function Fuyutsui:UpdateStateBlock(category, name)
    local cat = stateBlockGetters[category]
    if not cat then return end
    local getter = cat[name]
    if not getter then return end
    local key = bareKeyCategories[category] and name or (category .. name)
    local b = self.blocks
    local index = b and b.state and b.state[key]
    if not index then return end
    local value = getter(self)
    if value ~= nil then
        self:CreateTexture(index, value)
    end
end

--- 同一名称可能落在 状态 / 能量 / 物品 / 配置开关；无对应索引时会直接跳过
function Fuyutsui:UpdateBareStateBlock(name, categories)
    for i = 1, #categories do
        self:UpdateStateBlock(categories[i], name)
    end
end
