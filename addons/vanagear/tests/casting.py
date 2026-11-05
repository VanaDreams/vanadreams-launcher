r"""Off-game test: spells are never eaten by vanagear's hold on action packets.

    python addons/vanagear/tests/casting.py

The #bug-tickets report "vanagear error" (5 Nov 2026): a spell cast in the zone wait after login
threw at packethandlers.lua:158 (gSettings nil), the half-made action then threw at :252 on every
packet after it, and no spell or trust could be cast until a relog. Here an outgoing chunk holding a
position packet and then a Cure packet is fed through HandleOutgoingPacket under each case.
"""
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path = ...
package.path = addon_path .. '\\?.lua;' .. package.path
local ffi = require('ffi')
bit = require('bit')
function T(t) setmetatable(t, { __index = { contains = function(self, v) for _, x in ipairs(self) do if x == v then return true end end return false end } }) return t end
string.totable = function(s) local t = {} for i = 1, #s do t[i] = s:byte(i) end return t end
string.trim = function(s) return s end
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })

-- packets: an id in the low 9 bits of the first two bytes, the size / 4 in the next 7
local function packet(id, size, fields)
    local b = {}
    for i = 1, size do b[i] = 0 end
    b[1] = bit.band(id, 0xFF)
    b[2] = bit.bor(bit.rshift(id, 8), bit.lshift(size / 4, 1))
    for off, v in pairs(fields or {}) do b[off + 1] = bit.band(v, 0xFF); b[off + 2] = bit.rshift(v, 8) end
    return string.char(unpack(b))
end
local position = packet(0x15, 8)
local cure, chunk, raw
-- each case a new target, so the packet is never taken for the client resending the last one
local function target(t)
    cure = packet(0x1A, 16, { [0x08] = t, [0x0A] = 0x03, [0x0C] = 1 })
    chunk = position .. cure
    raw = ffi.new('uint8_t[?]', #chunk)
    ffi.copy(raw, chunk, #chunk)
end
target(5)

ashita = { bits = { unpack_be = function(r, off, bitoff, n)
    local v = ffi.cast('uint8_t*', r)[off] + ffi.cast('uint8_t*', r)[off + 1] * 256
    if (bitoff == 0) and (n == 9) then return bit.band(v, 0x1FF) end
    if (bitoff == 9) and (n == 7) then return bit.band(bit.rshift(v, 9), 0x7F) end
    return 0
end } }
struct = { unpack = function(fmt, s, pos)
    if (fmt == 'H') then return s:byte(pos) + s:byte(pos + 1) * 256 end
    if (fmt == 'B') then return s:byte(pos) end
    if (fmt == 'f') then return 0 end
    local n = tonumber(fmt:match('^c(%d+)$'))
    return s:sub(pos, pos + n - 1)
end }

local spells = { [1] = { CastTime = 8 } }
AshitaCore = {
    GetResourceManager = function() return { GetSpellById = function(_, id) return spells[id] end } end,
    GetPluginManager = function() return { Get = function() return nil end } end,
    GetPacketManager = function() return { AddOutgoingPacket = function(_, id) sent[#sent + 1] = id end } end,
}
local holding = false
gVanaCore = { Holding = function() return holding end, Moved = function() end, SawInjectedEquip = function() end }
local errors = 0
gVanaGuard = function(part, fn, ...) local ok = pcall(fn, ...); if not ok then errors = errors + 1 end; return ok end
gState = { DelayedEquip = {}, Injecting = false,
    HandleEquipEvent = function() end,
    Inject = function(id, data) gState.Injecting = true; AshitaCore:GetPacketManager():AddOutgoingPacket(id, data); gState.Injecting = false end }
gPacketHandlers = require('packethandlers')

-- one outgoing chunk; returns whether the Cure packet was blocked and which packets were injected
local function send()
    sent = {}
    local blocked = false
    local base = { chunk_data = chunk, chunk_data_raw = raw, chunk_size = #chunk, injected = false }
    for _, p in ipairs({ { 0x15, position, 0 }, { 0x1A, cure, #position } }) do
        local e = setmetatable({ id = p[1], data = p[2], size = #p[2], data_raw = raw + p[3] }, { __index = base })
        gPacketHandlers.HandleOutgoingPacket(e)
        if (p[1] == 0x1A) then blocked = (e.blocked == true) end
    end
    gState.PlayerAction = nil   -- the next case starts clean, as the action completes in game
    return string.format('blocked=%s injected=%d', tostring(blocked), #sent)
end
local nextTarget = 5
local function reset() gState.DelayedEquip = {}; gState.PlayerAction = nil; errors = 0; nextTarget = nextTarget + 1; target(nextTarget) end

local out = {}
-- 1. no profile yet (the zone wait at login), gSettings as the old Init left it: the spell goes as sent
gProfile = nil; gSettings = nil; reset()
out[#out + 1] = 'noprofile ' .. send() .. ' errors=' .. errors
-- 2. profile loaded: held back, precast, sent once
gProfile = {}; gSettings = { FastCast = 0, SpellOffset = 0.4, Debug = false }; reset()
out[#out + 1] = 'profile ' .. send() .. ' errors=' .. errors
-- 3. inside a zone wait with a profile: goes as sent
holding = true; reset()
out[#out + 1] = 'holding ' .. send() .. ' errors=' .. errors
holding = false
-- 4. something throws while the action is made (no spell resource): sent once anyway
spells[1] = nil; reset()
out[#out + 1] = 'throws ' .. send() .. ' errors=' .. errors
spells[1] = { CastTime = 8 }
-- 5. a half-made action left from an older throw (no Completion) does not jam the next spell
reset(); gState.PlayerAction = { Type = 'Spell' }
sent = {}
local base = { chunk_data = chunk, chunk_data_raw = raw, chunk_size = #chunk, injected = false }
local e1 = setmetatable({ id = 0x15, data = position, size = #position, data_raw = raw }, { __index = base })
gPacketHandlers.HandleOutgoingPacket(e1)
out[#out + 1] = 'stale injected=' .. #sent .. ' errors=' .. errors

-- 6. state.Init makes gSettings at load, before any profile
gSettings = nil
gDefaultSettings = { FastCast = 0, SpellOffset = 0.4 }
gActiveSettings = {}
ashita.memory = { find = function() return 0 end }
AshitaCore.GetInstallPath = function() return 'C:\\' end
AshitaCore.GetMemoryManager = function() return { GetParty = function() return { GetMemberIsActive = function() return 0 end } end } end
print = function() end
local state = require('state')
gState = state
state.Init()
out[#out + 1] = 'init gSettings=' .. tostring(gSettings ~= nil and gSettings.FastCast == 0)
return table.concat(out, '\n')
""", str(ADDON))
print(result)
want = [
    'noprofile blocked=false injected=0 errors=0',
    'profile blocked=true injected=1 errors=0',
    'holding blocked=false injected=0 errors=0',
    'throws blocked=true injected=1 errors=1',
    'stale injected=1 errors=0',
    'init gSettings=true',
]
print('PASS' if all(w in result for w in want) else 'FAIL')
