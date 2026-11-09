r"""Off-game test of vg\trusts.lua: the trusts in the party get sets built from the bags for
their job, level and what the server says they use (Engaged, and swap-ins: a set per spell kind,
weaponskill and job ability that reads gear, Ability for a tank; no Idle), your picks lay over them, only changed sets are sent, and the
lines carry the item ids in the server's slot order (Main 0 ... Back 15).

    python addons/vanagear/tests/trusts.py
"""
import tempfile
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent
TMP = tempfile.mkdtemp()

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path, tmp = ...
package.path = addon_path .. '/?.lua;' .. package.path
bit = require('bit')
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })
string.trimend = function(s, c) return (s:gsub(c .. '+$', '')) end
ashita = { fs = {
    exists = function(p) local f = io.open(p, 'r'); if f then f:close(); return true end return false end,
    create_directory = function(p) os.execute('mkdir "' .. p:gsub('/', '\\') .. '" 2>nul'); return true end,
} }
-- the trust profiles (vg\profile.lua) are named by the player; tests\profiles.py covers writing them
gState = { PlayerName = 'Me', PlayerId = 1000 }
function T(t) return setmetatable(t or {}, { __index = { append = function(self, v) self[#self + 1] = v end } }) end
gFileTools = require('filetools')
gSettings = { AddSetEquipScreenOrder = false }

local mods = dofile(addon_path .. '/data/mods.lua')
local ids = {}
for id, name in pairs(mods) do ids[name] = id end
package.loaded['vg.data'] = {
    ModId = function(name) return ids[name] end,
    SlotBit = (function() local t = {} for i = 1, 16 do t[i] = bit.lshift(1, i - 1) end return t end)(),
    Items = function() return {} end,
    Weaponskills = function() return { [42] = { key = 'savage_blade', skill = 3, level = 0, wsc = { STR = 0.5, MND = 0.5 }, hits = 2, kind = 'physical' } } end,
    Abilities = function() return { [48] = { key = 'sentinel', job = 7, level = 30, mods = { 'SENTINEL_EFFECT', 'ENHANCES_GUARDIAN' } } } end,
}
package.preload['encoding'] = function() return { ShiftJIS_To_UTF8 = function(_, s) return s end } end

local sent = {}
local jobs = { [1] = 'WAR', [3] = 'WHM', [7] = 'PLD' }
-- party: you (0), Excenmille the PLD/WAR trust (1), a player (2), Kupipi the WHM trust (3)
local members = {
    [0] = { id = 1000, name = 'Me', job = 1, lv = 99 },
    [1] = { id = 0x0100A001, name = 'Excenmille', job = 7, lv = 99, sub = 1 },
    [2] = { id = 1234, name = 'Friend', job = 3, lv = 99 },
    [3] = { id = 0x0100A002, name = 'Kupipi', job = 3, lv = 99, sub = 0 },
}
AshitaCore = {
    GetInstallPath = function() return tmp .. '/' end,
    GetMemoryManager = function() return {
        GetParty = function() return {
            GetMemberIsActive = function(_, i) return members[i] and 1 or 0 end,
            GetMemberServerId = function(_, i) return members[i] and members[i].id or 0 end,
            GetMemberTargetIndex = function(_, i) return 0x700 + i end,
            GetMemberName = function(_, i) return members[i].name end,
            GetMemberMainJob = function(_, i) return members[i].job end,
            GetMemberMainJobLevel = function(_, i) return members[i].lv end,
            GetMemberSubJob = function(_, i) return members[i].sub or 0 end,
        } end,
    } end,
    GetResourceManager = function() return { GetString = function(_, _, id) return jobs[id] or '' end } end,
    GetChatManager = function() return { QueueCommand = function(_, _, line) sent[#sent + 1] = line end } end,
}
gData = { Constants = { EquipSlotNames = { 'Main', 'Sub', 'Range', 'Ammo', 'Head', 'Body', 'Hands', 'Legs', 'Feet',
                                           'Neck', 'Waist', 'Ear1', 'Ear2', 'Ring1', 'Ring2', 'Back' } } }

local WAR, WHM, PLD = bit.lshift(1, 0), bit.lshift(1, 2), bit.lshift(1, 6)
local function piece(id, name, slot, jobMask, level, stats)
    local m = {}
    for k, v in pairs(stats) do m[ids[k]] = v end
    local mask = bit.lshift(1, slot)
    if slot == 13 then mask = mask + bit.lshift(1, 14) end -- rings go on either hand
    return { Id = id, Name = name, Equippable = true, Server = { slot = mask, jobs = jobMask, level = level, mods = m } }
end
gVana = {
    Who = { Job = 1 }, BuiltAt = 1, Prefs = {},
    Pieces = {
        piece(101, 'Fighter Mask', 4, WAR, 99, { ACC = 20, STORETP = 5 }),
        piece(102, 'Healer Cap', 4, WHM, 99, { CURE_POTENCY = 10, MND = 10 }),
        piece(103, 'Shared Ring', 13, WAR + WHM, 50, { ACC = 5, CURE_POTENCY = 5 }),
        piece(104, 'Shared Ring', 13, WAR + WHM, 50, { ACC = 5, CURE_POTENCY = 5 }),
        piece(105, 'High Belt', 10, WAR, 120, { ACC = 50 }),
        piece(106, 'Knight Helm', 4, PLD, 99, { DMGPHYS = -500, ENMITY = 6 }),
        piece(107, 'Strong Helm', 4, PLD, 99, { STR = 20, ATT = 30 }),
        piece(108, 'Bold Ring', 13, PLD, 1, { ENMITY = 5 }),
        piece(109, 'Guard Feet', 8, PLD, 1, { SENTINEL_EFFECT = 10 }),
    },
}

local trusts = require('vg.trusts')
local out = {}
local called = trusts.Called()
local exc, kup = called[1], called[2]
out[#out + 1] = 'called ' .. #called .. ' ' .. exc.Name .. ' ' .. kup.Name
-- nothing is sent until the server says the trust is yours; first it is asked about once
gVana.Who = { Job = 1 }
os.clock = (function() local c = 0 return function() c = c + 5 return c end end)()
-- four requests now go out (!trustgear info and !trustuse info for each trust), one per tick
trusts.Tick() trusts.Tick() trusts.Tick() trusts.Tick() trusts.Tick()
out[#out + 1] = table.concat(sent, ' ; ')
out[#out + 1] = 'ours ' .. tostring(trusts.Ours(exc)) .. ' ' .. tostring(trusts.Ours(kup))
-- the server's answer, hidden from chat
local e = { message = '\x1e\x01[VG trust] 16818177|Excenmille|7|1|99' }
trusts.TextIn(e)
local e2 = { message = '[VG uses] 16818177|flash:ability,cure_iv:cure,savage_blade:weaponskill,sentinel:ability' }
trusts.TextIn(e2)
out[#out + 1] = 'blocked ' .. tostring(e.blocked) .. ' ' .. tostring(e2.blocked) .. ' uses ' .. #trusts.Info(exc).Uses
local names = {}
for _, set in ipairs(trusts.Sets(exc)) do names[#names + 1] = set.Name end
out[#out + 1] = 'sets ' .. table.concat(names, ',')
out[#out + 1] = trusts.Pretty('cure_iv') .. '/' .. trusts.Key('Savage Blade')
sent = {}
for _ = 1, 12 do trusts.Tick() end
out[#out + 1] = table.concat(sent, ' ; ')
-- nothing changed: nothing more goes
sent = {}
for _ = 1, 4 do trusts.Tick() end
out[#out + 1] = 'again ' .. #sent
-- a pick changes one set; a set of your own is added and then deleted
trusts.SetSlot(exc, 'Engaged', 'Head', 'remove')
trusts.AddSet(exc, 'Flash')
trusts.SetSlot(exc, 'Flash', 'Head', 'Healer Cap')
for _ = 1, 4 do trusts.Tick() end
trusts.Reset(exc, 'Flash')
for _ = 1, 4 do trusts.Tick() end
out[#out + 1] = table.concat(sent, ' ; ')
-- the picks are on disk and come back after a reload
package.loaded['vg.trusts'] = nil
trusts = require('vg.trusts')
out[#out + 1] = 'reloaded ' .. tostring(trusts.Own(exc, 'Engaged').Head)
-- what the player switched off: the server's list is hidden from chat, shown as unticked boxes, and a change goes back
trusts.TextIn({ message = '\x1e\x01[VG trust] 16818177|Excenmille|7|1|99' })
trusts.TextIn({ message = '[VG uses] 16818177|flash:ability,cure_iv:cure,savage_blade:weaponskill' })
local eo = { message = '[VG off] 16818177|cureiv,weaponskill' }
trusts.TextIn(eo)
local flash, cure, wsk = { Name = 'flash' }, { Name = 'cure_iv' }, { Name = 'savage_blade' }
out[#out + 1] = 'off ' .. tostring(eo.blocked) .. ' cure ' .. tostring(trusts.IsOff(exc, cure)) .. ' flash ' .. tostring(trusts.IsOff(exc, flash))
sent = {}
trusts.SetOff(exc, flash, true)
trusts.SetOff(exc, cure, false)
trusts.SetOff(exc, flash, false)
trusts.SetOff(exc, wsk, true)
out[#out + 1] = 'now cure ' .. tostring(trusts.IsOff(exc, cure)) .. ' flash ' .. tostring(trusts.IsOff(exc, flash)) .. ' ws ' .. tostring(trusts.IsOff(exc, wsk))
for _ = 1, 12 do trusts.Tick() end
local useLines = {}
for _, l in ipairs(sent) do
    if string.find(l, '!trustuse %d+ o[nf]+ ') then useLines[#useLines + 1] = l end
end
out[#out + 1] = table.concat(useLines, ' ; ')

-- one user per piece: your sets first, then your trusts in party order, each from what is left
trusts.TextIn({ message = '[VG trust] 16818178|Kupipi|3|0|99' })
trusts.TextIn({ message = '[VG uses] 16818178|cure_iv:cure' })
local function rings(t, set)
    local n = 0
    for _, name in pairs(trusts.Final(t, set)) do if name == 'Shared Ring' then n = n + 1 end end
    return n
end
local function has(t, set, name)
    for _, n in pairs(trusts.Final(t, set)) do if n == name then return true end end
    return false
end
gProfile = nil
gVana.Sets = {}
out[#out + 1] = 'free: Kupipi cure rings ' .. rings(kup, 'Cure') .. ', Excenmille ability helm ' .. tostring(has(exc, 'Ability', 'Knight Helm'))
-- you wear one Shared Ring and the Knight Helm in your own sets
gProfile = { Sets = { Nuke = { Ring1 = 'Shared Ring' }, Tp_Default = { Head = 'Knight Helm' }, Idle = { Head = 'Knight Helm' } } }
out[#out + 1] = 'yours taken: Kupipi cure rings ' .. rings(kup, 'Cure') .. ', Excenmille ability helm ' .. tostring(has(exc, 'Ability', 'Knight Helm'))
-- you wear both rings: Kupipi's ring slots go empty
gProfile.Sets.Nuke.Ring2 = 'Shared Ring'
out[#out + 1] = 'both taken: Kupipi cure rings ' .. rings(kup, 'Cure')
-- the trust called first holds a Shared Ring (your pick for it): the one called after it gets the other copy
gProfile = nil
trusts.SetSlot(exc, 'Engaged', 'Ring2', 'Shared Ring')
out[#out + 1] = 'earlier trust holds one: Kupipi cure rings ' .. rings(kup, 'Cure')
-- no piece is promised more often than it is carried, across you and every trust
gProfile = { Sets = { Nuke = { Ring1 = 'Shared Ring' }, Tp_Default = { Head = 'Knight Helm' } } }
trusts.SetSlot(exc, 'Engaged', 'Ring2', nil)
local carried, promised = {}, {}
for _, p in ipairs(gVana.Pieces) do carried[p.Name] = (carried[p.Name] or 0) + 1 end
local function count(sets, into)
    local most = {}
    for _, set in pairs(sets) do
        local inSet = {}
        for _, name in pairs(set) do if type(name) == 'string' then inSet[name] = (inSet[name] or 0) + 1 end end
        for name, n in pairs(inSet) do if n > (most[name] or 0) then most[name] = n end end
    end
    for name, n in pairs(most) do into[name] = (into[name] or 0) + n end
end
count(gProfile.Sets, promised)
for _, t in ipairs({ exc, kup }) do
    local finals = {}
    for _, s in ipairs(trusts.Sets(t)) do finals[s.Name] = trusts.Final(t, s.Name) end
    count(finals, promised)
end
local over = {}
for name, n in pairs(promised) do
    -- the player's own picks are kept as they chose them; only what vanagear picks is shared out
    if (n > (carried[name] or 0)) and (name ~= 'Healer Cap') then over[#over + 1] = name .. ' ' .. n end
end
out[#out + 1] = 'over-promised: ' .. ((#over > 0) and table.concat(over, ', ') or 'none')
-- Manually equipped weapons/ammo are not in generated player sets, but still belong to the player.
gProfile = { Sets = { Idle = { Main = 'Player sword' } } }
gVana.Pieces = {
    { Name = 'Player sword' }, { Name = 'Player sword' }, { Name = 'Player sword' },
    { Name = 'Player animator' }, { Name = 'Player ammo' }, { Name = 'Spare ammo' },
}
local worn = { Main={Name='Player sword'},Sub={Name='Player sword'},Range={Name='Player animator'},Ammo={Name='Player ammo'} }
gData.GetEquipment = function() return worn end
local pool = trusts.Pool(exc)
local counts = {}
for _, p in ipairs(pool) do counts[p.Name]=(counts[p.Name] or 0)+1 end
assert(counts['Player sword']==1) -- two worn, not three from double-counting the profile
assert(counts['Player animator']==nil and counts['Player ammo']==nil and counts['Spare ammo']==1)
local before = trusts.Signature(exc)
worn.Ammo = {Name='Spare ammo'}
assert(trusts.Signature(exc) ~= before)
pool = trusts.Pool(exc); counts={}
for _, p in ipairs(pool) do counts[p.Name]=(counts[p.Name] or 0)+1 end
assert(counts['Player ammo']==1 and counts['Spare ammo']==nil)
before = trusts.Signature(exc); worn.Range=nil
assert(trusts.Signature(exc) ~= before)
return table.concat(out, '\n')
""", str(ADDON), TMP)
print(result)
lines = result.split('\n')
#              main sub rng ammo head body hands legs feet neck waist ear1 ear2 ring1 ring2 back
#   main sub rng ammo head body hands legs feet neck waist ear1 ear2 ring1 ring2 back
def line(set_name, ids):
    return f'/say !trustgear 16818177 {set_name} ' + ' '.join(str(i) for i in ids)
def head_ring(head, ring=0):
    return [0, 0, 0, 0, head] + [0] * 8 + [ring, 0, 0]
expect = [
    'called 2 Excenmille Kupipi',
    '/say !trustgear 16818177 info ; /say !trustuse 16818177 info ; /say !trustgear 16818178 info ; /say !trustuse 16818178 info',
    'ours false false',
    'blocked true true uses 4',
    'sets Engaged,Weaponskill,Cure,Ability,Savage Blade,Sentinel',
    'Cure IV/savageblade',
    # the tank's Engaged and Ability sets take the damage taken and enmity helm, its weaponskills the strong one;
    # the Shared Rings are WAR gear and Excenmille is PLD/WAR, so his support job's rings go on too
    ' ; '.join([
        line('engaged', [0, 0, 0, 0, 106] + [0] * 8 + [108, 103, 0]),
        line('weaponskill', [0, 0, 0, 0, 107] + [0] * 8 + [103, 103, 0]),
        line('cure', [0] * 13 + [103, 103, 0]),
        line('ability', head_ring(106, 108)),
        line('savageblade', [0, 0, 0, 0, 107] + [0] * 8 + [103, 103, 0]),
        line('sentinel', [0, 0, 0, 0, 0, 0, 0, 0, 109] + [0] * 7)]),
    'again 0',
    ' ; '.join([line('engaged', [0] * 13 + [108, 103, 0]), line('flash', head_ring(102)), '/say !trustgear 16818177 flash clear']),
    'reloaded remove',
    'off true cure true flash false',
    'now cure false flash false ws true',
    # flash was switched off and back on before anything was sent, so only its last word goes; the others go once each
    '/say !trustuse 16818177 on flash ; /say !trustuse 16818177 on cure_iv ; /say !trustuse 16818177 off savage_blade',
    # Excenmille is called first and wears both Shared Rings himself, so Kupipi, after him, gets what is left
    'free: Kupipi cure rings 0, Excenmille ability helm true',
    'yours taken: Kupipi cure rings 0, Excenmille ability helm false',
    'both taken: Kupipi cure rings 0',
    'earlier trust holds one: Kupipi cure rings 0',
    'over-promised: none',
]
assert lines == expect, '\n'.join(f'{a!r} vs {b!r}' for a, b in zip(lines, expect) if a != b)
print('PASS')
