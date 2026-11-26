name = "Vapor Horizon"
author = "zentraxxdev"
description = "Smooth magenta-to-cyan twilight gradient sweep"
accent = "#EC4899"

function render(u, v, time, speed, scale)
    local t = time * speed * 1.1
    local p = 0.5 + 0.5 * math.sin(u * 3.2 * scale + t)
    local r = math.floor(p * 236 + (1 - p) * 20)
    local g = math.floor(p * 72 + (1 - p) * 210)
    local b = math.floor(p * 153 + (1 - p) * 255)
    return r, g, b
end
