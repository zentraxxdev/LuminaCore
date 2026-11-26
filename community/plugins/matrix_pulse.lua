name = "Phosphor Pulse"
author = "zentraxxdev"
description = "Crisp emerald data pulses on dark phosphor background"
accent = "#10B981"

function render(u, v, time, speed, scale)
    local t = time * speed * 2.6
    local p = math.pow(math.max(0, math.sin(u * 8.0 * scale - t)), 5)
    local r = math.floor(4 + p * 35)
    local g = math.floor(28 + p * 225)
    local b = math.floor(14 + p * 120)
    return r, g, b
end
