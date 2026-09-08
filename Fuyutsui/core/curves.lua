local addon, ns = ...

local EnumPowerType = Fuyutsui.EnumPowerType
local curveCache = {}
local powerCurves = {}

function Fuyutsui:CreateColorCurve(point, b)
    local curve = C_CurveUtil.CreateColorCurve()
    curve:SetType(Enum.LuaCurveType.Linear)
    curve:AddPoint(0, CreateColor(0, 0, 0, 1))
    curve:AddPoint(point, CreateColor(0, 0, b / 255, 1))
    return curve
end

function Fuyutsui:CreateColorCurveScaling(b)
    if curveCache[b] then
        return curveCache[b]
    end
    local curve = C_CurveUtil.CreateColorCurve()
    curve:SetType(Enum.LuaCurveType.Linear)
    if b > 100 then
        curve:AddPoint(0, CreateColor(0, 0, (b - 100) / 255, 1))
        curve:AddPoint(1, CreateColor(0, 0, b / 255, 1))
    else
        local z = (100 - b) / 100
        curve:AddPoint(0, CreateColor(0, 0, 0, 1))
        curve:AddPoint(z, CreateColor(0, 0, 1 / 255, 1))
        curve:AddPoint(1, CreateColor(0, 0, b / 255, 1))
    end
    curveCache[b] = curve
    return curve
end

function Fuyutsui:CreatePowerCurve(powerType)
    if powerCurves[powerType] then return end
    local powerMax = UnitPowerMax("player", EnumPowerType[powerType])
    if powerMax >= 300 then
        powerCurves[powerType] = self:CreateColorCurve(1, 100)
    else
        powerCurves[powerType] = self:CreateColorCurve(1, powerMax)
    end
end

Fuyutsui.powerCurves = powerCurves

-- 队伍生命曲线必须在进入战斗前创建。战斗中只选择既有曲线，
-- 避免首次施放对应治疗法术时临时调用 C_CurveUtil.CreateColorCurve。
Fuyutsui.curve100 = Fuyutsui:CreateColorCurveScaling(100)
Fuyutsui.groupHealthCurves = {
    default = Fuyutsui.curve100,
    incoming15 = Fuyutsui:CreateColorCurveScaling(115),
    incoming40 = Fuyutsui:CreateColorCurveScaling(140),
}
Fuyutsui.curve255 = Fuyutsui:CreateColorCurve(255, 255)
Fuyutsui.castCurve = Fuyutsui:CreateColorCurve(25.5, 255)
Fuyutsui.curveMs = Fuyutsui:CreateColorCurve(2.55, 255)
