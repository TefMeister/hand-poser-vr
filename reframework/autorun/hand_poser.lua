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

local function write_state()
    last_state_t = now()
    write_state_now = false
    local st = {
        game = game,
        character = target and target.name or "",
        connected = target ~= nil,
        seq_seen = last_seq,
        frame = frame,
        error = last_error,
        joints = {},
        angles = {},
    }
    if target then
        for i, j in ipairs(target.joints) do st.joints[i] = {j.name, j.parent} end
        for name, _ in pairs(held) do st.angles[name] = angles_of(name) end
        for _, name in ipairs(want_read) do
            if st.angles[name] == nil then st.angles[name] = angles_of(name) end
        end
    end
    local ok, err = pcall(function() json.dump_file(STATE_FILE, st, 0) end)
    if ok then writes = writes + 1 else last_error = tostring(err) end
end

-- ---- holding the pose -------------------------------------------------------------------------------------------------

local function apply()
    -- 2026-10-25: how many joints this script holds, for other mods that pose the same joints (Visceral RE8's open hand
    -- stands down while a finger is held here)
    local n = 0
    for _ in pairs(held) do n = n + 1 end
    _G.hand_poser_held = (target ~= nil) and n or 0
    if not target or next(held) == nil then return end
    for name, v in pairs(held) do
        local j = target.by_name[name]
        if j then
            local ok = pcall(function() j:call("set_LocalEulerAngle", v) end)
            if not ok then target = nil; return end
        end
    end
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
    if write_state_now or now() - last_state_t > STATE_EVERY_SEC then
        local ok, err = pcall(write_state)
        if not ok then last_error = tostring(err) end
    end
end)

re.on_application_entry("LateUpdateBehavior", apply)
re.on_application_entry("PrepareRendering", apply)

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
