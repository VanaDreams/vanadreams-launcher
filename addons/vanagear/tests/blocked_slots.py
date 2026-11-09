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



EXTRA = r"""
local root=...
package.path=root..'/?.lua;'..package.path
bit=require('bit')
function T(t) return t end
package.preload['encoding']=function() return {} end
local masks={};for i=1,16 do masks[i]=2^(i-1) end
bonuses={}
package.loaded['vg.data']={SlotBit=masks,ModId=function() return 384 end,Items=function() return {} end,
 Augments=function() return {} end,GearSets=function() return bonuses end,Latents=function() return {} end,
 SlotBlocks=function() return dofile(root..'/data/slotblocks.lua') end}
score=require('vg.score')
who={Job=1,Sub=0,Level=99,Race=0,Zone=0}
function piece(id,slot,value,blocks)
 return {Id=id,Equippable=true,Server={slot=masks[slot],rslot=blocks,jobs=1,level=1,mods={[384]=value}}}
end
function build(pieces,slots)
 return score.Build(score.Compile({Name='Test',Stats={HASTE_GEAR=1}}),pieces,who,slots,{0})
end
"""


class GeneralBlockedSlots(unittest.TestCase):
    def lua(self, test):
        runtime=luajit21.LuaRuntime(unpack_returned_tuples=True)
        runtime.execute(EXTRA,str(ADDON))
        runtime.execute(test)

    def test_reverse_order_and_multislot_suit(self):
        self.lua(r"""
local cap=piece(1,5,100);local shirt=piece(2,6,100);local tunic=piece(3,6,300,16)
for _,slots in ipairs({{5,6},{6,5}}) do
 local set=build({cap,shirt,tunic},slots)
 assert(set[5]==nil and set[6].Id==3)
end
local suit=piece(4,6,500,16+64)
local set=build({cap,shirt,suit,piece(5,7,100)},{7,6,5})
assert(set[5]==nil and set[7]==nil and set[6].Id==4)
""")

    def test_sparse_server_masks_are_used_without_rebuilding_item_stats(self):
        self.lua(r"""
local cap=piece(12448,5,100)
local tunic=piece(12608,6,300) -- no inline rslot: comes from the actual generated exclusion table
local set=build({cap,tunic},{5,6})
assert(set[5]==nil and set[6].Id==12608)
""")

    def test_incompatible_set_bonus_trial_is_rejected(self):
        self.lua(r"""
bonuses[1]={items={1,3},min=2,mods={{mod=384,v={1000}}}}
local set,_,total=build({piece(1,5,100),piece(2,6,100),piece(3,6,150,16)},{5,6})
assert(set[5].Id==1 and set[6].Id==2 and math.abs(total-2)<0.000001)
""")

    def test_excluded_slots_and_unavailable_items_are_not_selected(self):
        self.lua(r"""
local unavailable=piece(3,6,900,16);unavailable.Equippable=false
local set=build({piece(1,5,100),piece(2,6,100),unavailable},{6})
assert(set[5]==nil and set[6].Id==2)
""")

    def test_many_candidate_combinations_remain_compatible_and_totals_match(self):
        self.lua(r"""
math.randomseed(74008)
for n=1,100 do
 local pieces={}
 for i=1,20 do
  local slot=5+((i-1)%5)
  local block=(math.random(4)==1) and 2^(4+math.random(5)-1) or 0
  pieces[i]=piece(70000+i,slot,math.random(1,500),block)
 end
 local set,_,total=build(pieces,{5,6,7,8,9})
 local sum=0
 for slot,p in pairs(set) do
  sum=sum+p.Server.mods[384]/100
  for other,q in pairs(set) do
   assert(slot==other or bit.band(p.Server.rslot or 0,2^(other-1))==0)
  end
 end
 assert(math.abs(total-sum)<0.000001)
end
""")


if __name__ == '__main__':
    unittest.main()
