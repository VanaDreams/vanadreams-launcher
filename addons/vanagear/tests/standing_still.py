r"""Off-game test: gear stops swapping while the player stands still.

    python addons/vanagear/tests/standing_still.py

1. Idle_Regen goes on under 60% HP. Its pieces carry less HP than the Idle set's, so wearing it lowers
   the max HP and lifts the share back over 60%: it came off, the max rose, the share fell, it went on
   again, every pass. Now it holds until 90% (vg\latch.lua).
2. A cloak takes the head (the server's rslot). A set with a head piece and a Twilight Cloak, or a head
   piece asked for while the cloak stays worn, sent the head every pass. Now nothing goes to a taken slot.
"""
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent
INSTALL = str(ADDON.parent.parent) + '\\'

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path, install = ...
package.path = addon_path .. '\\?.lua;' .. package.path
bit = require('bit')
function T(t) setmetatable(t, { __index = { append = function(self, v) self[#self + 1] = v end } }) return t end
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })
local out = {}

-- 1. the latch: 550 HP, the Idle set gives 1000 max, Idle_Regen gives 800
local latch = require('vg.latch')
local hp, regenOn, flips = 550, false, 0
for pass = 1, 20 do
    local max = regenOn and 800 or 1000
    local now = latch.Low('Idle_Regen', math.floor(hp * 100 / max))
    if (now ~= regenOn) then flips = flips + 1 end
    regenOn = now
end
out[#out + 1] = 'regen flips=' .. flips .. ' on=' .. tostring(regenOn)
-- healed to 90% in the Regen set: it comes off, and back in Idle (72%) it stays off
hp = 720
local off1 = latch.Low('Idle_Regen', 90)
local off2 = latch.Low('Idle_Regen', math.floor(hp * 100 / 1000))
out[#out + 1] = 'regen healed=' .. tostring(off1) .. ',' .. tostring(off2)
-- the old single line, for the record: it flips on every pass
local old, oldOn, oldFlips = nil, false, 0
for pass = 1, 20 do
    local max = oldOn and 800 or 1000
    local now = (math.floor(550 * 100 / max) < 60)
    if (now ~= oldOn) then oldFlips = oldFlips + 1 end
    oldOn = now
end
out[#out + 1] = 'old flips=' .. oldFlips

-- 2. the cloak: inventory 1 Nyame Helm, 2 Twilight Cloak
local items = { [1] = { Id = 23761, Index = 1, Count = 1, Flags = 0, Extra = '' },
                [2] = { Id = 11363, Index = 2, Count = 1, Flags = 0, Extra = '' } }
local res = { [23761] = { Name = { 'Nyame Helm' }, Flags = 0x800, Level = 99, Jobs = 0xFFFFFE, Slots = 16 },
              [11363] = { Name = { 'Twilight Cloak' }, Flags = 0x800, Level = 90, Jobs = 0xFFFFFE, Slots = 32 } }
local worn = {}   -- equipment slot (0-based) -> inventory index
local sent = {}
AshitaCore = {
    GetInstallPath = function() return install end,
    GetMemoryManager = function() return {
        GetInventory = function() return {
            GetContainerItem = function(_, c, i) if c == 0 then return items[i] or { Id = 0, Count = 0, Index = i } end return { Id = 0, Count = 0 } end,
            GetEquippedItem = function(_, slot) return { Index = worn[slot] or 0 } end,
        } end,
        GetPlayer = function() return {
            GetMainJob = function() return 5 end, GetJobLevel = function() return 99 end, GetMainJobLevel = function() return 99 end,
            GetSubJob = function() return 4 end, GetSubJobLevel = function() return 49 end,
        } end,
    } end,
    GetResourceManager = function() return { GetItemById = function(_, id) return res[id] end } end,
    GetConfigurationManager = function() return { GetInt32 = function() return 2 end } end,
    GetPacketManager = function() return { AddOutgoingPacket = function(_, id, p)
        if (id == 0x50) then sent[#sent + 1] = p[6] .. '<' .. p[5] end
        if (id == 0x51) then for k = 1, p[5] do sent[#sent + 1] = p[4 + k * 4 + 2] .. '<' .. p[4 + k * 4 + 1] end end
    end } end,
}
gSettings = { EquipBags = { 0 }, AllowSyncEquip = true, Debug = false }
gState = { Disabled = {}, Encumbrance = {} }
gData = { GetAugment = function() return { Type = 'Unaugmented' } end,
          GetContainerAvailable = function(c) return c == 0 end,
          GetContainerMax = function() return 2 end,
          Constants = { Containers = {} } }
gVana = {}
local equip = require('equip')
local clock = 0
os.clock = function() return clock end   -- each pass a second later, past the 0.2 s equip echo
local function wear(set)
    clock = clock + 1
    sent = {}
    local s = {}
    for slot, name in pairs(set) do s[slot] = equip.MakeItemTable(name) end
    equip.EquipSet(s, 'single')
    return table.concat(sent, ' ')
end
-- a set with both, nothing worn: only the cloak goes on (body is slot 4 in the packet)
out[#out + 1] = 'both=' .. wear({ [5] = 'Nyame Helm', [6] = 'Twilight Cloak' })
-- the cloak stays worn and a set asks only for the helm: nothing is sent
worn[5] = 2
out[#out + 1] = 'under cloak=' .. wear({ [5] = 'Nyame Helm' })
-- the cloak comes off for another body: the helm may go on again
worn[5] = nil
out[#out + 1] = 'no cloak=' .. wear({ [5] = 'Nyame Helm' })
return table.concat(out, '\n')
""", str(ADDON), INSTALL)
print(result)
want = ['regen flips=1 on=true', 'regen healed=false,false', 'old flips=20',
        'both=5<2', 'under cloak=\n', 'no cloak=4<1']
ok = all(w in result + '\n' for w in want)
print('PASS' if ok else 'FAIL')
