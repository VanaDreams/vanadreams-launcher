r"""Off-game test of equip.lua's weapon guard: with /vg weapons off no set changes Main, Sub or
Range, except a weapon the player picked in one of their own sets.

    python addons/vanagear/tests/weapons_off.py
"""
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path = ...
package.path = addon_path .. '/?.lua;' .. package.path
bit = require('bit')
function T(t) setmetatable(t, { __index = { append = function(self, v) self[#self + 1] = v end } }) return t end
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })

-- the bag: inventory slot 1 Nyame Helm (head), slot 2 Sanctity Necklace (neck); nothing equipped
local items = { [1] = { Id = 23761, Index = 1, Count = 1, Flags = 0, Extra = '' },
                [2] = { Id = 16000, Index = 2, Count = 1, Flags = 0, Extra = '' } }
local res = { [23761] = { Name = { 'Nyame Helm' }, Flags = 0x800, Level = 99, Jobs = 0xFFFFFE, Slots = 16 },
              [16000] = { Name = { 'Aymur' }, Flags = 0x800, Level = 99, Jobs = 0xFFFFFE, Slots = 1 } }
local sent = {}
AshitaCore = {
    GetMemoryManager = function() return {
        GetInventory = function() return {
            GetContainerItem = function(_, c, i) if c == 0 then return items[i] or { Id = 0, Count = 0, Index = i } end return { Id = 0, Count = 0 } end,
            GetEquippedItem = function() return { Index = 0 } end,
        } end,
        GetPlayer = function() return {
            GetMainJob = function() return 9 end, GetSubJob = function() return 0 end, GetJobLevel = function() return 99 end, GetMainJobLevel = function() return 99 end,
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
package.preload['encoding'] = function() return { ShiftJIS_To_UTF8 = function(_, s) return s end } end
local equip = require('equip')

-- the profile asks for a helm and a weapon
local function run(picks, weaponsOn, allow)
    sent = {}
    package.loaded['equip'] = nil
    local equip = require('equip')
    gVana = { Prefs = { Weapons = weaponsOn }, WeaponPicks = picks, WeaponAllow = allow }
    equip.EquipSet({ [5] = equip.MakeItemTable('Nyame Helm'), [1] = equip.MakeItemTable('Aymur') }, 'single')
    local out = {}
    for _, p in ipairs(sent) do out[#out + 1] = 'slot ' .. p.slot end
    return table.concat(out, ',')
end
gState = { Disabled = {}, Encumbrance = {} }
gData = { GetAugment = function() return { Type = 'Unaugmented' } end,
          GetContainerAvailable = function(c) return c == 0 end,
          GetContainerMax = function() return 2 end,
          Constants = { Containers = {} } }
gSettings = { EquipBags = { 0 }, AllowSyncEquip = true, Debug = false }
package.preload['encoding'] = function() return { ShiftJIS_To_UTF8 = function(_, s) return s end } end
return run({}, false) .. ' | ' .. run({ aymur = true }, false) .. ' | ' .. run({}, true) .. ' | ' .. run({}, false, { aymur = true })
""", str(ADDON))
print(result)
off, picked, on, allowed = [x.strip() for x in result.split('|')]
print('PASS' if off == 'slot 4' and 'slot 0' in picked and 'slot 0' in on and 'slot 0' in allowed else 'FAIL')
