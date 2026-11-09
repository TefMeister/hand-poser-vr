-- spot_names.lua -- checks the spot names the in-game script gives the window, with REFramework and Visceral RE8's bridge
-- pretended (no game needed): the area a free left hand is in (slot 213), "ready on <spot>" with LT up, old builds without slot 213.
--   lua test/spot_names.lua reframework/autorun/hand_poser.lua "hand_poser\\guns/"    (expected lines: test/spot_names.expected)
local script, gunsdir = arg[1], arg[2]
local frame_cb, entries = nil, {}
local state_out = nil
local files = {}   -- pretend reframework/data files: name -> table
re = { on_frame = function(f) frame_cb = f end, on_application_entry = function(n, f) entries[n] = f end,
       on_draw_ui = function() end, on_pre_application_entry = function() end }
log = { info = function() end, warn = function() end, error = function() end, debug = function() end }
imgui = setmetatable({}, { __index = function() return function() return false end end })
sdk = setmetatable({}, { __index = function() return function() return nil end end })
fs = { glob = function() return {} end }
json = {
    load_file = function(p) local t = files[p]; if t == nil then error("no file " .. p) end; return t end,
    dump_file = function(p, t) if p:find("state") then state_out = t end; files[p] = t; return true end,
}
Vector3f = { new = function(x, y, z) return { x = x, y = y, z = z } end }
local fake_t = 0; os.clock = function() return fake_t end
local slots = {}
_G.visceral_re8_bridge = { r = function(s) if slots[s] == nil then error("slot " .. s .. " out of range") end return slots[s] end }

local chunk = assert(loadfile(script))
chunk()
assert(frame_cb, "no on_frame registered")

local function run(n) for i = 1, n do fake_t = fake_t + 0.1; frame_cb() end end
local function spot() return state_out and state_out.spot or "(no state)" end

-- pose files kept by the window (reframework/data/hand_poser/guns/<name>.json)
local function kept(name, j) files[gunsdir .. name .. ".json"] = { joints = j or { L_IndexF1 = { 1, 2, 3 } } } end

-- 1. a free hand in the slide-from-the-left area, Village b101 (slot 213 = 18)
slots[3], slots[5], slots[36], slots[153], slots[184], slots[213] = 0, 0, 0, 14, 0, 18
run(20)
print("1 area 18        -> " .. spot())
-- 2. the same hand in no named area
slots[213] = 0; run(20)
print("2 area 0         -> " .. spot())
-- 3. an old Village build: no slot 213 at all
slots[213] = nil; run(20)
print("3 no slot 213    -> " .. spot())
-- 4. docked on the slide, LT up, no ready pose kept: the spot stays "slide"
slots[213] = 0; slots[153] = 3; run(20)
print("4 slide, no ready-> " .. spot())
-- 5. the same with "any gun @ ready" kept: "ready on slide"
kept("any gun @ ready"); run(60)
print("5 slide, LT up   -> " .. spot())
-- 6. LT held: the slide's own pose
slots[3] = 1; run(20)
print("6 slide, LT held -> " .. spot())
-- 7. a start sphere with the area known: ready at the area
slots[3] = 0; slots[153] = 2003; slots[213] = 12; run(60)
print("7 start 3, area  -> " .. spot())
-- 8. free, anywhere
slots[153] = 15; run(20)
print("8 free           -> " .. spot())
-- 9. the lookup chain: a pose kept for "no gun @ ready at slide from the left" is found for that area only
slots[153] = 14; slots[213] = 18; kept("no gun @ ready at slide from the left", { L_IndexF1 = { 9, 9, 9 } }); run(60)
local held = _G.hand_poser_held_all
print("9 specific pose  -> " .. spot() .. "  (own pose loaded for it: " .. tostring(files[gunsdir .. "no gun @ ready at slide from the left.json"] ~= nil) .. ")")
