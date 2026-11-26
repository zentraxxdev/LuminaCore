name = "Cyber Rain"
author = "zentraxxdev"
description = "Sharp cyan & indigo digital rain droplets along LED strips"
accent = "#00E5FF"

function render(u, v, time, speed, scale)
    local t = time * speed * 2.2
    local drop = math.max(0, math.sin((u * 6.0 * scale) - t))
    local tail = math.pow(drop, 4)
    local base = 0.2 + 0.2 * math.sin(u * 2.0 + t * 0.4)
    local r = math.floor(10 + tail * 40)
    local g = math.floor(35 * base + tail * 210)
    local b = math.floor(95 * base + tail * 160)
    return r, g, b
end
