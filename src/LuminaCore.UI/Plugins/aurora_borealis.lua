-- LuminaCore Hot-Reload Sample Shader Plugin
-- Edit & save this file while LuminaCore is running to see instant hot-reload!

name = "Aurora Borealis"
author = "LuminaCore"
description = "Northern lights emerald & violet ribbon (Lua Plugin)"
accent = "#10B981"

function render(u, v, time, speed, scale)
    local t = time * speed * 1.4
    local wave1 = 0.5 + 0.5 * math.sin(u * 5.2 * scale + t)
    local wave2 = 0.5 + 0.5 * math.cos(u * 3.1 * scale - t * 0.75 + wave1 * 1.6)
    local shimmer = 0.65 + 0.35 * math.sin(u * 9.0 - t * 2.1)

    local r = math.floor((wave2 * 115 + 15) * shimmer)
    local g = math.floor((wave1 * 215 + 35) * shimmer)
    local b = math.floor(((1.0 - wave1 * 0.4) * 195 + 45) * shimmer)
    return r, g, b
end
