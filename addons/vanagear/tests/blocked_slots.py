"""Off-game: a piece that takes another slot as well (the server's rslot, a Tunic takes the head) is never
built into a set with something in that slot (Alice, 5 Nov 2026: VanaGear put a Tunic and a Bronze Cap on
together and kept swapping them).

    python addons/vanagear/tests/blocked_slots.py
"""
from pathlib import Path
import unittest
from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent

LUA = r'''
    local root, tunicHaste = ...
    package.path = root .. '/?.lua;' .. package.path
    bit = require('bit')
    function T(t) return t end
    package.preload['encoding'] = function() return {} end
    local slotBit = {}
    for i = 1, 16 do slotBit[i] = bit.lshift(1, i - 1) end
    package.loaded['vg.data'] = {
        SlotBit = slotBit, ModId = function(n) return ({ HASTE_GEAR = 384 })[n] end,
        Items = function() return {} end, Augments = function() return {} end,
        GearSets = function() return {} end, Latents = function() return {} end,
    }
    local score = require('vg.score')
    local who = { Job = 1, Sub = 0, Level = 99, Race = 0, Zone = 0 }
    local function piece(id, slot, haste, rslot)
        return { Id = id, Equippable = true, Server = {
            slot = slotBit[slot], rslot = rslot, jobs = 1, level = 1, mods = { [384] = haste },
        } }
    end
    local HEAD, BODY = 5, 6
    local cap   = piece(1, HEAD, 100)
    local shirt = piece(2, BODY, 100)
    local tunic = piece(3, BODY, tunicHaste, slotBit[HEAD])
    local mode  = score.Compile({ Name = 'Tp_Default', Stats = { HASTE_GEAR = 1 } })
    local built = score.Build(mode, { cap, shirt, tunic }, who, { HEAD, BODY }, { 0 })
    return built[HEAD] and built[HEAD].Id or 0, built[BODY] and built[BODY].Id or 0
'''


class BlockedSlots(unittest.TestCase):
    def build(self, tunic_haste):
        lua = luajit21.LuaRuntime(unpack_returned_tuples=True)
        return lua.execute(LUA, str(ADDON), tunic_haste)

    def test_a_strong_tunic_leaves_the_head_empty(self):
        self.assertEqual(self.build(300), (0, 3))

    def test_a_weak_tunic_loses_to_cap_and_shirt(self):
        self.assertEqual(self.build(150), (1, 2))


if __name__ == '__main__':
    unittest.main()
