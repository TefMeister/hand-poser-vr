-- spot_names.lua -- checks the spot names the in-game script gives the window, with REFramework and Visceral RE8's bridge
-- pretended (no game needed): the area a free left hand is in (slot 213), "ready on <spot>" with LT up, old builds without slot 213,
-- both hands at the holsters (214 / 215), the right hand's gun zone (216), the block's guard (re8vr.wants_block), and which
-- pose file each hand's spot loads.
--   lua test/spot_names.lua reframework/autorun/hand_poser.lua "hand_poser\\guns/"    (expected lines: test/spot_names.expected)
local script, gunsdir = arg[1], arg[2]
local frame_cb, entries = nil, {}
local state_out = nil
local files = {}   -- pretend reframework/data files: name -> table
re = { on_frame = function(f) frame_cb = f end, on_application_entry = function(n, f) entries[n] = f end,
       on_draw_ui = function() end, on_pre_application_entry = function() end }
local last_log = ""
log = { info = function(m) last_log = m end, warn = function() end, error = function() end, debug = function() end }
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
-- 10. Village b104: a free left hand at the right hip holster
slots[213], slots[214], slots[215], slots[216] = 0, 2, 0, 0; slots[153] = 15; run(20)
print("10 left holster  -> " .. spot())
-- 11. a gun zone wins over a holster
slots[153] = 14; slots[213] = 12; run(20)
print("11 zone + holster-> " .. spot())
-- 12. the right hand in the gun's slide-from-the-left zone (the left hand holds the gun)
slots[153], slots[213], slots[214], slots[216] = 15, 0, 0, 18; run(20)
print("12 right zone    -> " .. spot())
-- 13. the right hand at the left shoulder holster, the left free
slots[216], slots[215] = 0, 4; run(20)
print("13 right holster -> " .. spot())
-- 14. both hands at holsters
slots[214] = 3; run(20)
print("14 both holsters -> " .. spot())
-- 15. bare hands: a pose kept for each hand's holster on its own, none for the two together: each hand loads its own
kept("no gun @ at left hip holster", { L_IndexF1 = { 5, 5, 5 } }); kept("no gun @ right hand at left shoulder holster", { R_IndexF1 = { 6, 6, 6 } })
slots[214], slots[215] = 0, 0; run(20); slots[214], slots[215] = 3, 4; run(60)
print("15 per-hand load -> " .. last_log)
-- 16. an older build with none of 214-216: as before
slots[214], slots[215], slots[216] = nil, nil, nil; slots[153] = 15; run(20)
print("16 no 214-216    -> " .. spot())
-- 17. the block held (praydog's re8vr.wants_block): both hands "guard", over a zone and a holster
slots[214], slots[215], slots[216] = 2, 4, 12; slots[153], slots[213] = 14, 12; _G.re8vr = { wants_block = true }; run(20)
print("17 guard         -> " .. spot())
-- 18. a guard pose kept for every gun is used with a gun in hand; with no gun only "no gun @ guard" counts
kept("any gun @ guard", { L_Hand = { 1, 1, 1 }, R_Hand = { 2, 2, 2 } }); _G.re8vr.wants_block = false; run(20); _G.re8vr.wants_block = true; run(60)
print("18 guard, no gun -> 'any gun @ guard' used: " .. tostring(last_log:find("guard", 1, true) ~= nil))
kept("no gun @ guard", { L_Hand = { 3, 3, 3 }, R_Hand = { 4, 4, 4 } }); _G.re8vr.wants_block = false; run(20); _G.re8vr.wants_block = true; run(60)
print("19 guard pose    -> " .. last_log)
-- 20. the block let go: back to the zones
_G.re8vr.wants_block = false; run(20)
print("20 block off     -> " .. spot())
