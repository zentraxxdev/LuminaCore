name = "Solar Flare"
author = "zentraxxdev"
description = "Molten amber & crimson corona waves"
accent = "#F97316"

function render(u, v, time, speed, scale)
    local t = time * speed * 1.5
    local w1 = 0.5 + 0.5 * math.sin(u * 4.5 * scale + t)
    local w2 = 0.5 + 0.5 * math.cos(u * 7.0 * scale - t * 1.3)
    local flare = math.pow(w1 * w2, 0.7)
    local r = math.floor(140 + flare * 115)
    local g = math.floor(20 + flare * 135)
    local b = math.floor(5 + flare * 20)
    return r, g, b
end
