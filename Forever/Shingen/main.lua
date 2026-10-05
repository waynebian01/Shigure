local addon, ns = ...

function Shingen:RefreshPlayerState()
    self.isInitialized = false
    self.state.isDead = UnitIsDeadOrGhost("player")
    self.state.isChatOpen = false
    self.state.drinkStatus = false
    self.state.mountCasting = false
    self:RefreshPlayerMountedState()
    self:RefreshPlayerPetState()
    self:RefreshPlayerCombatState()
    self:SetPlayerMoving(IsPlayerMoving())
    self:RefreshPlayerCastStateBlocks()
    self:UpdatePlayerHealth()
    self:RefreshAssistedCombatSuggestion()
    self:RefreshTargetTypeState()
    self:RefreshFocusTypeState()
    self:RefreshGroupTypeState()
    self:RefreshGroupCountState()
    self:RefreshPlayerBars()
    self:RefreshShapeshiftFormState()
    self:UpdatePlayerStagger()
    self:UpdateRune()
    self:RefreshTargetRangeState()
    self:RefreshFocusRangeState()
    self:RefreshTargetHealthState()
    self:RefreshFocusHealthState()
    self:RefreshTargetPowerState()
    self:RefreshFocusPowerState()
    self:RefreshBossUnitStates()
    self:RebuildGroupRoster()
    self:RefreshAllPlayerPowers()
    C_Timer.After(1, function()
        self:RefreshPlayerConfigStateBlocks()
        self.isInitialized = true
    end)
end

-- 载入玩家 blocks 配置
function Shingen:LoadPlayerBlocks(specIndex)
    if not specIndex or not self.ClassBlocks then
        return
    end
    local t = self.ClassBlocks[specIndex]
    if not t then return end
    local blocks = {
        state = {},
        items = {},
        auras = {},
        spells = {},
        counts = {},
        nameplates = nil,
    }

    local index = 1

    -- states 支持分类表：状态/特殊/能量/配置开关/目标/焦点/鼠标/宠物/首领1-5
    -- blocks.state 键：基础分类用名称本身；单位分类用 分类..名称（如 目标生命值）
    if type(t.states) == "table" then
        local stateCategoryOrder = {
            "状态", "特殊", "能量", "配置开关", "目标", "焦点", "鼠标", "宠物",
            "首领1", "首领2", "首领3", "首领4", "首领5",
        }
        local bareKeyCategories = {
            ["状态"] = true,
            ["特殊"] = true,
            ["能量"] = true,
            ["配置开关"] = true,
        }
        local nested = t.states["状态"] or t.states["特殊"] or t.states["能量"]
            or t.states["配置开关"] or t.states["目标"] or t.states["焦点"] or t.states["鼠标"]
            or t.states["宠物"]
            or t.states["首领1"] or t.states["首领2"] or t.states["首领3"]
            or t.states["首领4"] or t.states["首领5"]
        if nested then
            for _, category in ipairs(stateCategoryOrder) do
                local list = t.states[category]
                if type(list) == "table" then
                    for _, name in ipairs(list) do
                        if name then
                            local key = bareKeyCategories[category] and name or (category .. name)
                            blocks.state[key] = index
                            local rgbSpell = name == "施法技能" and (category == "目标" or category == "焦点"
                                or category == "首领1" or category == "首领2" or category == "首领3"
                                or category == "首领4" or category == "首领5")
                            index = index + (rgbSpell and 2 or 1)
                        end
                    end
                end
            end
        else
            for _, name in ipairs(t.states) do
                if name then
                    blocks.state[name] = index
                    index = index + 1
                end
            end
        end
    end

    -- auras 支持：
    --   旧：{ { spellId=... }, ... }  → 视为 player / HELPFUL
    --   新：{ player={...}, target={ harmful={...}, helpful={...} }, focus={...}, boss1..boss5={ harmful={...} } }
    if type(t.auras) == "table" then
        local function AppendAuraList(list, unit, filter)
            if type(list) ~= "table" then return end
            for _, aura in ipairs(list) do
                if type(aura) == "table" and (aura.spellId or aura.spellIds) then
                    local auraFilter = filter
                    if aura.isPlayer == true then
                        auraFilter = auraFilter .. "|PLAYER"
                    end
                    local hasApplications = aura.maxApps == true
                    blocks.auras[index] = {
                        name = aura.name,
                        spellId = aura.spellId,
                        spellIds = aura.spellIds,
                        maxApps = hasApplications and aura.maxApps or nil,
                        unit = unit,
                        filter = auraFilter,
                    }
                    index = index + (hasApplications and 2 or 1)
                else
                    print("LoadPlayerBlocks: aura 缺少 spellId/spellIds，已跳过")
                end
            end
        end

        local nested = t.auras.player or t.auras.target or t.auras.focus
        if not nested then
            for bossIndex = 1, 5 do
                if t.auras["boss" .. bossIndex] then
                    nested = true
                    break
                end
            end
        end
        if nested then
            AppendAuraList(t.auras.player, "player", "HELPFUL")
            if type(t.auras.target) == "table" then
                AppendAuraList(t.auras.target.harmful, "target", "HARMFUL")
                AppendAuraList(t.auras.target.helpful, "target", "HELPFUL")
            end
            if type(t.auras.focus) == "table" then
                AppendAuraList(t.auras.focus.harmful, "focus", "HARMFUL")
                AppendAuraList(t.auras.focus.helpful, "focus", "HELPFUL")
            end
            for bossIndex = 1, 5 do
                local unit = "boss" .. bossIndex
                local boss = t.auras[unit]
                if type(boss) == "table" then
                    AppendAuraList(boss.harmful, unit, "HARMFUL")
                end
            end
        else
            AppendAuraList(t.auras, "player", "HELPFUL")
        end
    end

    local countSpellIds = {}
    if type(t.spells) == "table" then
        for _, spell in ipairs(t.spells) do
            if type(spell) ~= "table" or not spell.spellId then
                print("LoadPlayerBlocks: spell 缺少 spellId，已跳过")
            else
                local spellId = spell.spellId
                if not blocks.spells[spellId] then
                    blocks.spells[spellId] = {}
                end
                if spell.charge then
                    blocks.spells[spellId].index = index
                    blocks.spells[spellId].charge = index + 1
                    index = index + 2
                else
                    blocks.spells[spellId].index = index
                    index = index + 1
                end
                if spell.forcedKnown then
                    blocks.spells[spellId].forcedKnown = spell.forcedKnown
                end
                if spell.inSpellBook then
                    blocks.spells[spellId].inSpellBook = spell.inSpellBook
                end
                local countType
                if spell.charge == true then
                    countType = "charge"
                elseif spell.castCount == true then
                    countType = "castCount"
                end
                if countType and not countSpellIds[spellId] then
                    countSpellIds[spellId] = true
                    blocks.spells[spellId].count = index
                    tinsert(blocks.counts, { valueType = countType, spellId = spellId, index = index })
                    index = index + 1
                end
            end
        end
    end

    if type(t.items) == "table" then
        local itemIDs = {}
        for itemID, info in pairs(t.items) do
            if type(itemID) == "number" and itemID > 0 and itemID % 1 == 0
                and type(info) == "table" and type(info.name) == "string" and info.name ~= "" then
                itemIDs[#itemIDs + 1] = itemID
            else
                print("LoadPlayerBlocks: item 缺少有效 itemId/name，已跳过")
            end
        end
        table.sort(itemIDs)
        local seenNames = {}
        for _, itemID in ipairs(itemIDs) do
            local info = t.items[itemID]
            if seenNames[info.name] then
                print("LoadPlayerBlocks: item 名称“" .. info.name .. "”重复，已跳过")
            elseif blocks.state[info.name] then
                print("LoadPlayerBlocks: item 名称“" .. info.name .. "”与状态字段重复，已跳过")
            else
                seenNames[info.name] = true
                blocks.state[info.name] = index
                blocks.items[itemID] = {
                    index = index,
                    name = info.name,
                    isEquipped = info.isEquipped == true,
                }
                index = index + 1
            end
        end
    end

    if type(t.group) == "table" then
        blocks.groups = {
            start = index,
            num = 0,
            aura = {},
        }
        local groups = blocks.groups
        local stateFields = t.group.state
        if type(stateFields) ~= "table" then
            stateFields = {}
            for _, field in ipairs({ "healthPercent", "role", "dispel", "class", "dispelMagic", "dispelCurse", "dispelDisease", "dispelPoison", "dispelBleed" }) do
                local configured = tonumber(t.group[field])
                if configured and configured >= 0 then stateFields[#stateFields + 1] = field end
            end
        end
        local dispelFields = { "dispelMagic", "dispelCurse", "dispelDisease", "dispelPoison", "dispelBleed" }
        local supported = { healthPercent = true, role = true, class = true,
            dispelMagic = true, dispelCurse = true, dispelDisease = true, dispelPoison = true, dispelBleed = true }
        local function AddGroupStateField(field)
            -- 旧驱散在原位置展开；与 C# 的布局迁移保持一致。
            if field == "dispel" then
                for _, dispelField in ipairs(dispelFields) do AddGroupStateField(dispelField) end
            elseif type(field) == "string" and supported[field] and not groups[field] then
                groups.num = groups.num + 1
                groups[field] = groups.num
            end
        end
        for _, field in ipairs(stateFields) do AddGroupStateField(field) end
        -- 队伍启用时生命值和职责始终占位；与 config 转换器保持相同的追加顺序。
        for _, field in ipairs({ "healthPercent", "role" }) do
            if not groups[field] then
                groups.num = groups.num + 1
                groups[field] = groups.num
            end
        end
        -- 兼容旧的 [4]/[5] 光环键；按配置顺序压紧，不依赖键值作为像素偏移。
        local auraKeys = {}
        for key, aura in pairs(t.group.aura or {}) do
            if type(key) == "number" and type(aura) == "table" then
                auraKeys[#auraKeys + 1] = key
            end
        end
        table.sort(auraKeys)
        for _, key in ipairs(auraKeys) do
            local aura = t.group.aura[key]
            local hasSpell = type(aura.spellId) == "number" and aura.spellId > 0
            if type(aura.spellIds) == "table" then
                for _, spellId in ipairs(aura.spellIds) do
                    if type(spellId) == "number" and spellId > 0 then hasSpell = true end
                end
            end
            if hasSpell then
                groups.num = groups.num + 1
                groups.aura[groups.num] = aura
                if aura.maxApps == true then
                    groups.num = groups.num + 1
                end
            end
        end
        -- 队伍偏移从 1 开始；预留插件实际处理的 40 人后再追加姓名板。
        if groups.num > 0 then
            local groupEnd = index + 40 * groups.num + 1
            if groupEnd - 1 > (self.MainPixelMaxCount or self.MainPixelCount) then
                print("LoadPlayerBlocks: 队伍像素超出主像素行上限，已停用队伍")
                blocks.groups = nil
            else
                index = groupEnd
            end
        else
            blocks.groups = nil
        end
    end

    if type(t.nameplates) == "table" then
        -- 先 7 格单位映射，再生命值/距离/战斗固定占用每个单位前三格，光环从第四格开始。
        local mappingCount = self.NameplateMappingFieldCount or 7
        blocks.nameplates = {
            start = index,
            healthPercent = 1,
            range = 2,
            combat = 3,
            num = 3,
            auras = {},
        }
        blocks.nameplates.auraStart = blocks.nameplates.num + 1
        if type(t.nameplates.auras) == "table" then
            for _, aura in ipairs(t.nameplates.auras) do
                local hasSpell = type(aura) == "table" and type(aura.spellId) == "number" and aura.spellId > 0
                if type(aura) == "table" and type(aura.spellIds) == "table" then
                    for _, spellId in ipairs(aura.spellIds) do
                        if type(spellId) == "number" and spellId > 0 then hasSpell = true end
                    end
                end
                if hasSpell then
                    local valueOffset = blocks.nameplates.num + 1
                    blocks.nameplates.num = valueOffset
                    local maxApps = aura.maxApps == true and true or nil
                    tinsert(blocks.nameplates.auras, {
                        name = aura.name,
                        spellId = aura.spellId,
                        spellIds = aura.spellIds,
                        isPlayer = aura.isPlayer == true,
                        maxApps = maxApps,
                        valueOffset = valueOffset,
                    })
                    if maxApps then blocks.nameplates.num = blocks.nameplates.num + 1 end
                end
            end
        end
        if t.nameplates.threat == true then
            blocks.nameplates.num = blocks.nameplates.num + 1
            blocks.nameplates.threatOffset = blocks.nameplates.num
        end
        if t.nameplates.castSpell == true then
            blocks.nameplates.num = blocks.nameplates.num + 1
            blocks.nameplates.castSpellOffset = blocks.nameplates.num
            blocks.nameplates.num = blocks.nameplates.num + 1
        end
        if t.nameplates.castCountdown == true then
            blocks.nameplates.num = blocks.nameplates.num + 1
            blocks.nameplates.castCountdownOffset = blocks.nameplates.num
        end
        local maxPixels = self.MainPixelMaxCount or self.MainPixelCount
        local nameplateEnd = index + mappingCount + self.NameplateSlotCount * blocks.nameplates.num
        if nameplateEnd - 1 > maxPixels then
            print("LoadPlayerBlocks: 姓名板像素超出主像素行 " .. maxPixels .. " 格上限，已停用姓名板")
            blocks.nameplates = nil
        else
            index = nameplateEnd
        end
    end

    -- 先按实际占用选定容量档位（可能改变格宽），再重建光环容器，避免容器按旧格宽定位。
    if self.ApplyMainPixelCapacity then
        self:ApplyMainPixelCapacity(index - 1)
    end

    self.blocks = blocks
    if self.ReleaseUnitAuraContainers then
        self:ReleaseUnitAuraContainers()
    elseif self.ReleasePlayerAuraContainers then
        self:ReleasePlayerAuraContainers()
    end
    if self.ReleaseGroupAuraContainers then
        self:ReleaseGroupAuraContainers()
    end
    if self.LoadNameplatePixels then
        self:LoadNameplatePixels(blocks.nameplates)
        self:RefreshNameplatePixels()
    end
end

-- 载入玩家宏（按当前职业选取；宏本身不再按专精分组）
function Shingen:LoadPlayerMacros()
    local classFile = UnitClassBase("player")
    local m = self.ClassMacros and self.ClassMacros[classFile]
    if not m then
        return
    end
    local dynamicSpells = m.dynamicSpells
    self.MacrosList = {
        dynamicSpells = dynamicSpells,
        staticSpells = m.staticSpells,
        specialSpells = m.specialSpells,
    }
    self:CreateMacro(dynamicSpells, m.staticSpells, m.specialSpells)
end
