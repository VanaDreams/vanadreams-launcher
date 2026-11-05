r"""Off-game test of equip.lua's fallback: a profile names a piece the player does not carry,
and vanagear's built piece for that slot is equipped instead.

    python addons/vanagear/tests/equip_fallback.py
"""
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path = ...
package.path = addon_path .. '\\?.lua;' .. package.path
bit = require('bit')
function T(t) setmetatable(t, { __index = { append = function(self, v) self[#self + 1] = v end } }) return t end
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })

-- the bag: inventory slot 1 Nyame Helm (head), slot 2 Sanctity Necklace (neck); nothing equipped
local items = { [1] = { Id = 23761, Index = 1, Count = 1, Flags = 0, Extra = '' },
                [2] = { Id = 23515, Index = 2, Count = 1, Flags = 0, Extra = '' } }
local res = { [23761] = { Name = { 'Nyame Helm' }, Flags = 0x800, Level = 99, Jobs = 0xFFFFFE, Slots = 16 },
              [23515] = { Name = { 'Sanctity Necklace' }, Flags = 0x800, Level = 99, Jobs = 0xFFFFFE, Slots = 512 } }
local sent = {}
AshitaCore = {
    GetMemoryManager = function() return {
        GetInventory = function() return {
            GetContainerItem = function(_, c, i) if c == 0 then return items[i] or { Id = 0, Count = 0, Index = i } end return { Id = 0, Count = 0 } end,
            GetEquippedItem = function() return { Index = 0 } end,
        } end,
        GetPlayer = function() return {
            GetMainJob = function() return 9 end, GetJobLevel = function() return 99 end, GetMainJobLevel = function() return 99 end,
            GetSubJob = function() return 19 end,
        } end,
    } end,
    GetResourceManager = function() return { GetItemById = function(_, id) return res[id] end } end,
    GetConfigurationManager = function() return { GetInt32 = function() return 2 end } end,
    GetPacketManager = function() return { AddOutgoingPacket = function(_, id, p) sent[#sent + 1] = { id = id, slot = p[6], index = p[5] } end } end,
}
gSettings = { EquipBags = { 0 }, AllowSyncEquip = true, Debug = false }
gState = { Disabled = {}, Encumbrance = {} }
gData = { GetAugment = function() return { Type = 'Unaugmented' } end,
          GetContainerAvailable = function(c) return c == 0 end,
          GetContainerMax = function() return 2 end,
          Constants = { Containers = {} } }
gVana = {}
local equip = require('equip')

-- the profile asks for a head piece the player does not have, and the right neck piece
local set = { [5] = equip.MakeItemTable('Valorous Mask'), [10] = equip.MakeItemTable('Sanctity Necklace') }
-- vanagear built Nyame Helm for the head
equip.SetFallback({ [5] = equip.MakeItemTable({ Name = 'Nyame Helm', Bag = 0 }) })
equip.EquipSet(set, 'single')
equip.SetFallback(nil)

local out = {}
for _, p in ipairs(sent) do out[#out + 1] = string.format('slot %d <- index %d', p.slot, p.index) end
return table.concat(out, '; ') .. ' | missing: ' .. table.concat(gVana.LastMissing or {}, ',')
""", str(ADDON))
print(result)
expect_head = 'slot 4 <- index 1' in result
expect_neck = 'slot 9 <- index 2' in result
print('PASS' if expect_head and expect_neck and 'valorous mask' in result else 'FAIL')
