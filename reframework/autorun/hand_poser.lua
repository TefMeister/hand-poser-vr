-- hand_poser.lua -- the in-game half of Hand Poser (UNTESTED in a real game as of 2026-10-16).
--
-- It finds the player, lists the skeleton, and holds whichever joints the Hand Poser window asks for at the angles it
-- asks for. The two halves talk through files under reframework\data\hand_poser\ (the only place REFramework Lua may
-- read and write):
--   state.json    written here about once a second (and right after a new command): game, character, every joint as
--                 [name, parent index], and the live local Euler angles (degrees) of the joints the window is holding
--                 or asked to read.
--   command.json  written by the window on every change: {seq, hold={name={x,y,z} degrees...}, want_read={names},
--                 go_name="..." (optional: pose a named game object instead of the player)}.
-- The held angles are written at LateUpdateBehavior and again at PrepareRendering, after the animation has had its
-- say: the timing alphaZomega's EMV Engine poser proved, and the one Visceral RE2's finger poses use. Everything else
-- about the skeleton is left alone, so removing a joint from the window hands it straight back to the game.
--
-- Inspired by the poser in EMV Engine by alphaZomega (MIT); written fresh, nothing copied.

local DIR = "hand_poser\\"
local STATE_FILE = DIR .. "state.json"
local COMMAND_FILE = DIR .. "command.json"
local POLL_EVERY_FRAMES = 3        -- how often command.json is looked at
local STATE_EVERY_SEC = 1.0        -- how often state.json is written when nothing changed
local STATE_LIVE_SEC = 0.12        -- 2026-10-25: while joints are held or read, often enough for live numbers in the window
local RELINK_EVERY_SEC = 2.0       -- how often the player is looked for again when it was not found
local DEG = 180.0 / math.pi

local game = (reframework and reframework.get_game_name and reframework:get_game_name()) or "unknown"
local ns = sdk.game_namespace

local last_seq = -1
local held = {}            -- joint name -> Vector3f (radians), the pose to keep
local want_read = {}       -- joint names the window wants the live angles of
local go_name = nil        -- a named game object to pose instead of the player
local target = nil         -- {go=, xform=, name=, joints={ {obj=, name=, parent=} ... }, by_name={} }
local last_state_t = 0
-- 2026-10-25 (Tefa: "how do i save hand poses for guns individually?", then "yes please make it automatic!"): the gun in the
-- right hand is reported, so the app keeps one pose per gun and swaps them when another gun is drawn. Village: the VR mode's
-- re8vr.weapon (its GameObject's name, e.g. "ri3042_Inventory"); RE2 / RE3: the same field on their VR table if there is one.
-- "" = no gun (or a game where it cannot be read: then every pose is the "no gun" one)
local gun_now, gun_checked_frame = "", -100
-- 2026-11-01: Visceral RE2 / RE3's bridge slots: the gun in hand's WP number, the left hand on the gun (1)
local VB_GUN, VB_LEFT = 84, 539
if tostring(game):lower() == "re3" then VB_GUN, VB_LEFT = 57, 58 end
local function current_gun()
    local name = ""
    for _, tname in ipairs({ "re8vr", "re2vr", "re3vr", "re2" }) do
        local v = rawget(_G, tname)
        if type(v) == "table" or type(v) == "userdata" then
            -- the weapon's GameObject (get_GameObject, else its <owner> field), whose name is the gun; a GameObject answers
            -- get_Name itself (the way Visceral RE8's grip script reads it)
            local w = nil
            pcall(function() w = v.weapon end)
            if w then
                local go = nil
                pcall(function() go = w:call("get_GameObject") end)
                if go == nil then pcall(function() go = w:get_field("<owner>k__BackingField") end) end
                local n = nil
                if go then pcall(function() n = go:call("get_Name") end) end
                if n == nil then pcall(function() n = w:call("get_Name") end) end
                if n then name = tostring(n) end
            end
            if name ~= "" then break end
        end
    end
    -- 2026-11-01 RE2 / RE3 (Visceral's bridge): the plugin writes the gun in hand's WP number to slot VB_GUN (-1 = none)
    if name == "" then
        local vb = rawget(_G, "visceral_bridge")
        if type(vb) == "table" and type(vb.r) == "function" then
            local ok, wp = pcall(vb.r, VB_GUN)
            if ok and type(wp) == "number" and wp >= 0 then name = string.format("wp%04d", math.floor(wp + 0.5)) end
        end
    end
    return name
end
local last_relink_t = 0
local frame = 0
local last_error = ""
local write_state_now = false
local writes = 0

local function note(err)
    last_error = tostring(err)
    write_state_now = true
end

local function now()
    return os.clock()
end

-- ---- finding the thing to pose --------------------------------------------------------------------------------------

local function scene()
    local sm = sdk.get_native_singleton("via.SceneManager")
    local t = sdk.find_type_definition("via.SceneManager")
    if not sm or not t then return nil end
    return sdk.call_native_func(sm, t, "get_CurrentScene")
end

local function as_game_object(obj)
    if obj == nil then return nil end
    local td = obj:get_type_definition()
    if td and td:is_a("via.GameObject") then return obj end
    local ok, go = pcall(function() return obj:call("get_GameObject") end)
    if ok and go then return go end
    return nil
end

-- The player, by the route each RE game uses (EMV Engine's map, re-typed; only RE2 and RE3 are the ones we run).
local function find_player()
    local pm = sdk.get_managed_singleton(ns("PlayerManager"))
    if pm then
        local ok, p = pcall(function() return pm:call("get_CurrentPlayer") end)
        if ok and p then return as_game_object(p) end
    end
    local cm = sdk.get_managed_singleton("chainsaw.CharacterManager")   -- RE4
    if cm then
        local ok, go = pcall(function()
            return cm:call("getPlayerContextRef"):call("get_BodyGameObject")
        end)
        if ok and go then return go end
    end
    local sc = scene()
    if sc then
        for _, tname in ipairs({"app.PlayerUpdaterBase", "app.PlayerCamera"}) do   -- RE8, RE7
            local t = sdk.find_type_definition(tname)
            if t then
                local ok, arr = pcall(function() return sc:call("findComponents(System.Type)", sdk.typeof(tname)) end)
                if ok and arr then
                    local els = arr:get_elements()
                    if els and els[1] then return as_game_object(els[1]) end
                end
            end
        end
    end
    return nil
end

local function find_named(name)
    local sc = scene()
    if not sc then return nil end
    local ok, go = pcall(function() return sc:call("findGameObject(System.String)", name) end)
    if ok then return go end
    return nil
end

local function list_joints(xform)
    local arr = xform:call("get_Joints")
    if not arr then return nil end
    local els = arr:get_elements()
    if not els then return nil end
    local joints, by_name, index_of = {}, {}, {}
    for i, j in ipairs(els) do
        index_of[j:get_address()] = i - 1
    end
    for i, j in ipairs(els) do
        local name = j:call("get_Name") or ("joint" .. i)
        local parent = -1
        local ok, p = pcall(function() return j:call("get_Parent") end)
        if ok and p then parent = index_of[p:get_address()] or -1 end
        joints[i] = {obj = j, name = name, parent = parent}
        if by_name[name] == nil then by_name[name] = j end
    end
    return joints, by_name
end

local function relink()
    last_relink_t = now()
    local go = go_name and find_named(go_name) or find_player()
    if not go then target = nil; return end
    local ok, err = pcall(function()
        local xform = go:call("get_Transform")
        local joints, by_name = list_joints(xform)
        if not joints then error("no joints on " .. tostring(go:call("get_Name"))) end
        target = {go = go, xform = xform, name = go:call("get_Name") or "?", joints = joints, by_name = by_name}
    end)
    if not ok then target = nil; note(err) end
    write_state_now = true
end

local function target_alive()
    if not target then return false end
    local ok, alive = pcall(function() return target.xform ~= nil and target.go:call("get_Valid") ~= false end)
    return ok and alive
end

-- ---- the two files ----------------------------------------------------------------------------------------------------

local function read_command()
    local cmd = json.load_file(COMMAND_FILE)
    if type(cmd) ~= "table" or cmd.seq == nil or cmd.seq == last_seq then return end
    last_seq = cmd.seq
    spot_unposed = false   -- the app is holding a pose: tuning wins (2026-11-08)
    held = {}
    for name, v in pairs(cmd.hold or {}) do
        if type(v) == "table" and #v == 3 then
            held[name] = Vector3f.new(v[1] / DEG, v[2] / DEG, v[3] / DEG)
        end
    end
    want_read = {}
    for _, name in ipairs(cmd.want_read or {}) do want_read[#want_read + 1] = name end
    local new_go = cmd.go_name
    if new_go == "" then new_go = nil end
    if new_go ~= go_name then go_name = new_go; target = nil end
    write_state_now = true
end

local function angles_of(name)
    local j = target and target.by_name[name]
    if not j then return nil end
    local ok, e = pcall(function() return j:call("get_LocalEulerAngle") end)
    if not ok or not e then return nil end
    return {e.x * DEG, e.y * DEG, e.z * DEG}
end

-- 2026-10-25 (Tefa: "can the other sliders that are affected also change their values in real time as i change something
-- else that is also affected by this group of moving parts?"): a joint's own (local) angles never change when a joint above it
-- turns, so each held or read joint is also reported as turned "from the hand" (relative to that side's hand joint), which
-- does change live. Euler angles in degrees, X then Y then Z, from the relative quaternion.
local HAND_NAMES = { l = { "L_Hand", "l_hand", "l_arm_wrist" }, r = { "R_Hand", "r_hand", "r_arm_wrist" } }
local function hand_of(name)
    local side = name:sub(1, 2):lower()
    local list = side == "l_" and HAND_NAMES.l or side == "r_" and HAND_NAMES.r or nil
    if not list or not target then return nil end
    for _, n in ipairs(list) do
        local j = target.by_name[n]
        if j and n ~= name then return j end
    end
    return nil
end
local function euler_deg(q)   -- x, y, z, w -> degrees about X, Y, Z (applied Z then Y then X)
    local x, y, z, w = q.x, q.y, q.z, q.w
    local sx = 2 * (w * x + y * z); local cx = 1 - 2 * (x * x + y * y)
    local sy = 2 * (w * y - z * x); if sy > 1 then sy = 1 elseif sy < -1 then sy = -1 end
    local sz = 2 * (w * z + x * y); local cz = 1 - 2 * (y * y + z * z)
    return { math.atan(sx, cx) * DEG, math.asin(sy) * DEG, math.atan(sz, cz) * DEG }
end
local function from_hand(name)
    local j = target and target.by_name[name]
    local h = hand_of(name)
    if not j or not h then return nil end
    local ok, out = pcall(function()
        local a, b = h:call("get_Rotation"), j:call("get_Rotation")
        -- inverse(a) * b, written out (unit quaternions)
        local ax, ay, az, aw = -a.x, -a.y, -a.z, a.w
        local q = { x = aw * b.x + ax * b.w + ay * b.z - az * b.y, y = aw * b.y - ax * b.z + ay * b.w + az * b.x,
                    z = aw * b.z + ax * b.y - ay * b.x + az * b.w, w = aw * b.w - ax * b.x - ay * b.y - az * b.z }
        return euler_deg(q)
    end)
    return ok and out or nil
end

-- 2026-10-26 (Tefa: "automatic saving for hand poses on different land spots and different start grab spots"): where the
-- left hand is, read from Visceral RE8's bridge (slot 153, src/pairs.cpp), so the app keeps one pose per gun AND spot.
-- "" = nowhere in particular (or no Visceral bridge): the gun's own pose. Pair names come from visceral_re8\pairs.json.
local SPOT_SLOT = 153
local spot_now, spot_checked_frame, pair_names, pair_names_t = "", -100, {}, -100
local function pair_name(n)
    if now() - pair_names_t > 2.0 then
        pair_names_t = now()
        local ok, d = pcall(json.load_file, "visceral_re8/pairs.json")
        if ok and type(d) == "table" and type(d.pairs) == "table" then
            pair_names = {}
            for _, p in ipairs(d.pairs) do if type(p) == "table" and p.n then pair_names[math.floor(p.n)] = tostring(p.name or "") end end
        end
    end
    local nm = pair_names[n]
    return (nm and nm ~= "" and nm ~= ("pair " .. n)) and (" " .. nm) or ""
end
local function current_spot()
    local b = rawget(_G, "visceral_re8_bridge")
    if type(b) ~= "table" or type(b.r) ~= "function" then return "" end
    local ok, v = pcall(b.r, SPOT_SLOT)
    if not ok or type(v) ~= "number" then return "" end
    v = math.floor(v + 0.5)
    if v >= 2000 then return "start " .. (v - 2000) .. pair_name(v - 2000) end
    if v >= 1000 then return "land " .. (v - 1000) .. pair_name(v - 1000) end
    if v == 3 then return "slide" end
    if v == 4 then return "magazine" end
    if v == 5 then return "handle" end
    if v == 6 then return "drum" end
    if v == 7 then return "insert point" end
    if v == 8 then return "slide from the left" end   -- 2026-10-28: a pistol's slide taken from the gun hand's left (Visceral RE8)
    if v == 9 then return "rounds from the pouch" end   -- 2026-10-28: rounds held in the left hand (Visceral RE8)
    if v == 10 then return "hammer" end   -- 2026-11-02: the Wolfsbane's hammer, the left palm on it (Visceral RE8)
    -- 2026-11-08 (Tefa: "a point on the guns when something happens, then the hand pose changes"): Visceral RE8's stage spots
    if v == 11 then return "slide at the back" end   -- the slide / pump / bolt from its click at the back until it is home
    if v == 12 then return "slide from the left at the back" end
    if v == 13 then return "magazine in hand" end   -- a magazine carried in the left hand (after B / LT, or from the pouch)
    return ""
end
-- 2026-11-02 (Tefa: "same with the right hand when it docks on the gun to insert bullets"): Visceral RE8's slot 184, the right
-- hand's spot; it joins the left's in the spot name, so a pose is kept for each pair of places
local RIGHT_SPOT_SLOT = 184
-- 2026-11-08 (Tefa, the Village ideas list: "the trigger finger follows RT"): Visceral RE8's slot 5 = the right trigger pressed.
-- Held this long with a gun out and the right hand nowhere else, the right hand's spot is "trigger pulled", so the window keeps a
-- pose for it (pose the right index finger there); in play that pose's right index finger is eased in on every RT press (below).
local RT_SLOT, TRIGGER_POSE_HOLD_SEC = 5, 1.0
local rt_since = nil
local function rt_held()
    local b = rawget(_G, "visceral_re8_bridge")
    if type(b) ~= "table" or type(b.r) ~= "function" then return false end
    local ok, v = pcall(b.r, RT_SLOT)
    return ok and type(v) == "number" and v > 0.5
end
local function right_spot()
    local b = rawget(_G, "visceral_re8_bridge")
    if type(b) ~= "table" or type(b.r) ~= "function" then return "" end
    local ok, v = pcall(b.r, RIGHT_SPOT_SLOT)
    if not ok or type(v) ~= "number" then return "" end
    v = math.floor(v + 0.5)
    if v == 0 and gun_now ~= "" and rt_held() then
        rt_since = rt_since or now()
        if now() - rt_since >= TRIGGER_POSE_HOLD_SEC then return "trigger pulled" end
        return ""
    end
    rt_since = nil
    if v == 1 then return "right hand on the cover" end
    if v == 2 then return "right hand on the bolt" end
    if v == 3 then return "right hand over the hammer" end   -- 2026-11-06: the Wolfsbane's right hand lifted over the hammer, the cover open (Visceral RE8 b075)
    if v == 4 then return "right hand on the bolt at the back" end   -- 2026-11-08: from the bolt's click at the back until it is home
    return ""
end

-- 2026-11-01 (Tefa: "yes": the finger poses without the app open): each gun's (and spot's) pose is loaded HERE too, from
-- data/hand_poser/guns/<gun>[ @ <spot>].json (the app keeps a copy of every gun pose there; REFramework Lua may read only
-- under reframework/data), whenever the gun or the left hand's spot changes. The same choice as the app's (PoserUi
-- GunCheck): the spot's own pose if it has one; a spot with none keeps the pose in hand; no spot = the gun's own. With the
-- app open its command follows a moment later with the same pose and wins, so tuning works as before.
local GUNS_DIR = DIR .. "guns/"
local own_key, own_loads = nil, 0
-- 2026-11-08: a spot where the hand is NOT on the gun's grip; with no pose saved for it the left hand is let go (the game's open /
-- close works on it, as before the spot existed) instead of keeping the pose in hand. The app's own commands still hold it (tuning).
local FREE_UNLESS_POSED = { ["magazine in hand"] = true }
local spot_unposed = false
local function safe_name(n) return (n:gsub('[<>:"/\\|%?%*]', "_"):gsub("^%s+", ""):gsub("%s+$", "")) end
local function load_own_pose()
    local key = (gun_now == "" and "no gun" or gun_now) .. (spot_now ~= "" and (" @ " .. spot_now) or "")
    if key == own_key then return end
    own_key = key
    if spot_now:find("trigger pulled", 1, true) then return end   -- 2026-11-08: the trigger finger is eased in by itself (pulled_frame)
    local ok, d = pcall(json.load_file, GUNS_DIR .. safe_name(key) .. ".json")
    local found = ok and type(d) == "table" and type(d.joints) == "table"
    spot_unposed = (not found) and FREE_UNLESS_POSED[spot_now] == true
    if not found then return end   -- none kept: the pose in hand stays
    held = {}
    for name, v in pairs(d.joints) do
        if type(v) == "table" and #v == 3 then held[name] = Vector3f.new(v[1] / DEG, v[2] / DEG, v[3] / DEG) end
    end
    own_loads = own_loads + 1
    if own_loads <= 50 then log.info("[hand_poser] own pose loaded: " .. key) end
end

local function write_state()
    last_state_t = now()
    write_state_now = false
    local st = {
        game = game,
        character = target and target.name or "",
        connected = target ~= nil,
        gun = gun_now,
        spot = spot_now,
        seq_seen = last_seq,
        frame = frame,
        error = last_error,
        joints = {},
        angles = {},
        rel = {},
    }
    if target then
        for i, j in ipairs(target.joints) do st.joints[i] = {j.name, j.parent} end
        for name, _ in pairs(held) do st.angles[name] = angles_of(name) end
        for _, name in ipairs(want_read) do
            if st.angles[name] == nil then st.angles[name] = angles_of(name) end
        end
        for name, _ in pairs(st.angles) do st.rel[name] = from_hand(name) end
    end
    local ok, err = pcall(function() json.dump_file(STATE_FILE, st, 0) end)
    if ok then writes = writes + 1 else last_error = tostring(err) end
end

-- ---- holding the pose -------------------------------------------------------------------------------------------------

-- 2026-11-01 (Tefa: "hand opening and closing was not working for me after i tuned the left hand and left hand off the
-- weapon seemed to not go back into a default pose, but stayed as the last tuned pose"): a hand's fingers are held only while
-- that hand is on the gun: the left while it is docked anywhere on it (a spot, or the two-handed front grip: Visceral RE8's
-- bridge slots 153 and 36), the right while a gun is in hand. A free hand is the game's again, and Visceral RE8's open / close
-- with the grip button works on it. Visceral RE2's bridge says the same for RE2 (slot 539). With neither, everything is held.
local function hands_on_gun()
    local vb = rawget(_G, "visceral_bridge")   -- 2026-11-01 Visceral RE2: slot 539 = the left hand is on the gun
    if type(vb) == "table" and type(vb.r) == "function" and rawget(_G, "visceral_re8_bridge") == nil then
        local ok, l = pcall(vb.r, VB_LEFT)
        return ok and type(l) == "number" and l > 0.5, gun_now ~= ""
    end
    local b = rawget(_G, "visceral_re8_bridge")
    if type(b) ~= "table" or type(b.r) ~= "function" then return true, true end
    local ok1, sp = pcall(b.r, SPOT_SLOT)
    local ok2, g = pcall(b.r, 36)
    local left = (ok1 and type(sp) == "number" and sp > 0.5) or (ok2 and type(g) == "number" and g > 0.5)
    if spot_unposed then left = false end   -- 2026-11-08: a magazine in hand with no pose of its own: the hand is free
    return left, gun_now ~= ""
end

-- 2026-11-08: the trigger finger. The gun's "trigger pulled" pose (data/hand_poser/guns/<gun> @ trigger pulled.json, kept by the
-- window) gives the right index finger's pulled angles; on every RT press it eases from where it is (our held pose, or the game's
-- own grip) to them, and back on release. Visceral RE8 reports RT as pressed / not pressed only, so the finger eases over
-- TRIGGER_EASE_SEC rather than following the trigger's travel. A gun with no such pose: nothing changes.
local TRIGGER_EASE_SEC, PULLED_CHECK_SEC = 0.06, 2.0
local pulled, pulled_gun, pulled_t, trig_k, trig_t = {}, nil, -100, 0.0, nil
local trig_base = {}   -- joint name -> Vector3f, the finger as it was when the press began (its blend's start)
local function is_trigger_finger(name) return name:sub(1, 2):lower() == "r_" and name:lower():find("index", 1, true) ~= nil end
local function load_pulled()
    if gun_now == pulled_gun and now() - pulled_t < PULLED_CHECK_SEC then return end
    pulled_gun, pulled_t, pulled = gun_now, now(), {}
    if gun_now == "" then return end
    local ok, d = pcall(json.load_file, GUNS_DIR .. safe_name(gun_now .. " @ trigger pulled") .. ".json")
    if not ok or type(d) ~= "table" or type(d.joints) ~= "table" then return end
    for name, v in pairs(d.joints) do
        if is_trigger_finger(name) and type(v) == "table" and #v == 3 then pulled[name] = Vector3f.new(v[1] / DEG, v[2] / DEG, v[3] / DEG) end
    end
end
local function wrap(a) return (a + math.pi) % (2 * math.pi) - math.pi end
local function pulled_frame(first_pass, right_on)
    local t = now()
    local dt = trig_t and math.min(0.1, t - trig_t) or 0.0
    local was = trig_k
    if first_pass then
        trig_t = t
        pcall(load_pulled)
        local want = (right_on and rt_held()) and 1.0 or 0.0
        local step = dt / TRIGGER_EASE_SEC
        if trig_k < want then trig_k = math.min(want, trig_k + step) else trig_k = math.max(want, trig_k - step) end
    end
    if not target or next(pulled) == nil then trig_base = {}; return end
    -- the finger's start is read once, as a press begins (the game's own angle, or our held one), so a game that leaves the
    -- joint alone between frames cannot make the blend creep; on the way back it is written exactly once more, then let go
    if first_pass and was <= 0.0 and trig_k > 0.0 then
        trig_base = {}
        for name, _ in pairs(pulled) do
            local j = target.by_name[name]
            local ok, e = j and pcall(function() return j:call("get_LocalEulerAngle") end)
            if ok and e then trig_base[name] = held[name] or e end
        end
    end
    if trig_k <= 0.0 and next(trig_base) == nil then return end
    for name, p in pairs(pulled) do
        local j, b = target.by_name[name], trig_base[name]
        if j and b then
            local k = right_on and trig_k or 0.0
            local v = Vector3f.new(b.x + wrap(p.x - b.x) * k, b.y + wrap(p.y - b.y) * k, b.z + wrap(p.z - b.z) * k)
            pcall(function() j:call("set_LocalEulerAngle", v) end)
        end
    end
    if trig_k <= 0.0 and not first_pass then trig_base = {} end   -- home again (both passes written): let the finger go
end

local function apply(first_pass)
    -- 2026-10-25: how many joints this script holds, for other mods that pose the same joints (Visceral RE8's open hand
    -- stands down while a finger is held here). 2026-11-01: the LEFT hand's joints actually held (that open hand is the left's)
    local left_on, right_on = hands_on_gun()
    -- 2026-11-05 (Tefa, Visceral RE8: "when docked and shells in my hand, the fingers don't move at all"): while Visceral RE8 plays
    -- the game's own finger movement on a hand (its bridge slot 186: +1 left, +2 right), that hand is left to it
    local b = rawget(_G, "visceral_re8_bridge")
    if type(b) == "table" and type(b.r) == "function" then
        local ok, c = pcall(b.r, 186)
        c = ok and type(c) == "number" and math.floor(c + 0.5) or 0
        if c % 2 == 1 then left_on = false end
        if c >= 2 then right_on = false end
    end
    local nl, n = 0, 0
    if not target or next(held) == nil then _G.hand_poser_held = 0; _G.hand_poser_held_all = 0; pulled_frame(first_pass, right_on); return end
    for name, v in pairs(held) do
        local side = name:sub(1, 2):lower()
        if (side ~= "l_" or left_on) and (side ~= "r_" or right_on) then
            local j = target.by_name[name]
            if j then
                local ok = pcall(function() j:call("set_LocalEulerAngle", v) end)
                if not ok then target = nil; _G.hand_poser_held = 0; return end
                n = n + 1
                if side == "l_" then nl = nl + 1 end
            end
        end
    end
    _G.hand_poser_held = nl
    _G.hand_poser_held_all = n
    pulled_frame(first_pass, right_on)   -- 2026-11-08: after the held pose, so the trigger finger blends from it
end

re.on_frame(function()
    frame = frame + 1
    if frame % POLL_EVERY_FRAMES == 0 then
        local ok, err = pcall(read_command)
        if not ok then note(err) end
    end
    if not target_alive() then
        target = nil
        if now() - last_relink_t > RELINK_EVERY_SEC then relink() end
    end
    if frame - gun_checked_frame >= 15 then   -- a gun drawn or put away: told to the app straight away
        gun_checked_frame = frame
        local g = current_gun()
        if g ~= gun_now then gun_now = g; write_state_now = true end
    end
    if frame - spot_checked_frame >= 3 then   -- the hand lands in a fraction of a second: its spot is told quickly
        spot_checked_frame = frame
        local sp = current_spot()
        local rs = right_spot()
        if rs ~= "" then sp = sp ~= "" and (sp .. " + " .. rs) or rs end
        if sp ~= spot_now then spot_now = sp; write_state_now = true end
    end
    if gun_checked_frame == frame or spot_checked_frame == frame then pcall(load_own_pose) end
    local every = (next(held) ~= nil or #want_read > 0) and STATE_LIVE_SEC or STATE_EVERY_SEC
    if write_state_now or now() - last_state_t > every then
        local ok, err = pcall(write_state)
        if not ok then last_error = tostring(err) end
    end
end)

re.on_application_entry("LateUpdateBehavior", function() apply(true) end)
re.on_application_entry("PrepareRendering", function() apply(false) end)

re.on_draw_ui(function()
    if imgui.tree_node("Hand Poser") then
        imgui.text(string.format("%s  %s  held %d  seq %d  state writes %d",
            game, target and target.name or "(no character)", (function() local n = 0 for _ in pairs(held) do n = n + 1 end return n end)(),
            last_seq, writes))
        if last_error ~= "" then imgui.text("last error: " .. last_error) end
        if imgui.button("Let go of everything") then held = {}; write_state_now = true end
        imgui.tree_pop()
    end
end)
