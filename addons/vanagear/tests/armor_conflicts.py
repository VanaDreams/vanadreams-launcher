"""Off-game armour conflicts through real func/equip code and a simulated equipment server."""
from pathlib import Path
import unittest
from lupa import luajit21

ADDON=Path(__file__).resolve().parent.parent
LUA=r'''
local root=...
package.path=root..'/?.lua;'..package.path
bit=require('bit')
function T(t) return setmetatable(t or {},{__index={append=function(self,v) self[#self+1]=v end}}) end
chat=setmetatable({},{__index=function() return function(...) return table.concat({...},' ') end end})
package.preload['encoding']=function() return {ShiftJIS_To_UTF8=function(_,s) return s end} end
local names={'Main','Sub','Range','Ammo','Head','Body','Hands','Legs','Feet','Neck','Waist','Ear1','Ear2','Ring1','Ring2','Back'}
local slots={}; for i,n in ipairs(names) do slots[n]=i; slots[n:lower()]=i end
items,res,worn,sent={},{},{},{}
local function item(index,id,name,slot,mask)
 items[index]={Id=id,Index=index,Count=1,Flags=0,Extra=''}
 res[id]={Name={name},Flags=0x800,Level=1,Jobs=0xFFFFFE,Slots=2^(slot-1)}
 return mask
end
local masks=dofile(root..'/data/slotblocks.lua')
item(1,12448,'Bronze Cap',5)
item(2,12608,'Tunic',6)
item(3,60003,'Shirt',6)
item(4,60004,'Head and hands',5); masks[60004]=64
item(5,60005,'Gloves',7)
item(6,60006,'Trousers',8); masks[60006]=256
item(7,60007,'Boots',9)
item(8,60008,'Full suit',6); masks[60008]=16+64+128+256
item(9,60009,'Necklace',10)
item(10,21372,'Dunna',3)
item(11,18136,'Morion Tathlum',4)
local function blocks(id,slot) return bit.band(masks[id] or 0,2^(slot-1))~=0 end
local function apply(slot,index)
 sent[#sent+1]={slot=slot,index=index}
 if index==0 then worn[slot]=nil;return end
 local id=items[index].Id
 for other,old in pairs(worn) do
  if other~=slot and (blocks(id,other) or blocks(items[old].Id,slot)) then worn[other]=nil end
 end
 worn[slot]=index
end
AshitaCore={
 GetInstallPath=function() return root..'/../../' end,
 GetMemoryManager=function() return {
  GetInventory=function() return {
   GetContainerItem=function(_,c,i) return c==0 and items[i] or nil end,
   GetEquippedItem=function(_,i) return {Index=worn[i+1] or 0} end,
  } end,
  GetPlayer=function() return {GetMainJob=function() return 9 end,GetSubJob=function() return 19 end,
   GetJobLevel=function() return 99 end,GetMainJobLevel=function() return 99 end} end,
 } end,
 GetResourceManager=function() return {GetItemById=function(_,id) return res[id] end} end,
 GetConfigurationManager=function() return {GetInt32=function() return 2 end} end,
 GetPacketManager=function() return {AddOutgoingPacket=function(_,id,p)
  if id==0x50 then apply(p[6]+1,p[5])
  elseif id==0x51 then for i=1,p[5] do local o=4+i*4+1;apply(p[o+1]+1,p[o]) end
  else error('Unexpected packet '..id) end
 end} end,
}
gSettings={EquipBags={0},AllowSyncEquip=true,Debug=false}
gState={Disabled={},Encumbrance={},DelayedEquip={}}
gData={Constants={EquipSlotNames=names,EquipSlots=slots,EquipSlotsLC=slots,Containers={}},
 GetEquipSlot=function(n) return slots[n] or 0 end,
 GetAugment=function() return {Type='Unaugmented'} end,
 GetContainerAvailable=function(c) return c==0 end,GetContainerMax=function() return 11 end}
gVana={Prefs={Weapons=false},SlotBlocks=masks,Sets={},Own={}}
gEquip=require('equip');gFunc=require('func');vp=require('vg.profile')
local clock=0;os.clock=function() return clock end
function loadSets(built,own)
 local merged,marks=vp.Merge(own or {},built or {},true)
 gProfile={Sets=merged};gVana.Sets=merged;gVana.Own=marks
 vp.MarkSources(merged,marks,built or {})
 return merged
end
function pass(callback,style,delta)
 gEquip.ClearBuffer();callback();gEquip.ProcessBuffer(style or 'single');clock=clock+(delta or 0.3)
end
function single(slot,piece) pass(function() gFunc.Equip(slot,piece) end) end
function stable(callback,style)
 for i=1,20 do pass(callback,style) end
end
'''

class ArmourConflicts(unittest.TestCase):
 def run_lua(self,test):
  L=luajit21.LuaRuntime(unpack_returned_tuples=True);L.execute(LUA,str(ADDON));L.execute(test)

 def test_stale_generated_conflict_settles_without_repeated_packets(self):
  self.run_lua(r'''
for _,style in ipairs({'single','set','auto'}) do
 worn={};sent={}
 local s=loadSets({Idle={Head='Bronze Cap',Body='Tunic'}})
 stable(function() gFunc.EquipSet(s.Idle) end,style)
 assert(#sent==1 and worn[5]==1 and worn[6]==nil,style..' kept swapping')
end
''')

 def test_authored_head_beats_generated_body_and_reverse(self):
  self.run_lua(r'''
local s=loadSets({Idle={Head='Bronze Cap',Body='Tunic'}},{Idle={Head='Bronze Cap'}})
stable(function() gFunc.EquipSet(s.Idle) end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
s=loadSets({Idle={Head='Bronze Cap',Body='Tunic'}},{Idle={Body='Tunic'}})
sent={};stable(function() gFunc.EquipSet(s.Idle) end)
assert(worn[5]==nil and worn[6]==2 and #sent==1)
''')

 def test_later_generated_overlay_cannot_override_authored_piece(self):
  self.run_lua(r'''
local s=loadSets({Idle={Head='Bronze Cap'},Overlay={Body='Tunic'}},{Idle={Head='Bronze Cap'}})
stable(function() gFunc.EquipSet(s.Idle);gFunc.EquipSet(s.Overlay) end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
''')

 def test_explicit_later_call_and_priority_decide_conflicting_choices(self):
  self.run_lua(r'''
stable(function() gFunc.EquipSet({Body='Tunic'});gFunc.Equip('Head','Bronze Cap') end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
sent={};stable(function() gFunc.EquipSet({Head='Bronze Cap'});gFunc.Equip('Body','Tunic') end)
assert(worn[6]==2 and worn[5]==nil and #sent==1)
sent={};stable(function() gFunc.EquipSet({Head={Name='Bronze Cap',Priority=10}});gFunc.Equip('Body','Tunic') end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
''')

 def test_worn_tunic_is_not_a_hidden_lock_and_cache_tracks_implicit_removal(self):
  self.run_lua(r'''
single('Body','Tunic');sent={}
pass(function() gFunc.Equip('Head','Bronze Cap') end,'set',0.01)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
assert(gEquip.GetCurrentEquip(6).Item==nil)
stable(function() gFunc.Equip('Head','Bronze Cap') end)
assert(#sent==1)
''')

 def test_real_locks_and_encumbrance_are_respected(self):
  self.run_lua(r'''
single('Body','Tunic');sent={};gState.Disabled[6]=true
stable(function() gFunc.Equip('Head','Bronze Cap') end)
assert(worn[6]==2 and worn[5]==nil and #sent==0)
gState.Disabled[6]=false;gState.Encumbrance[6]=true
stable(function() gFunc.Equip('Head','Bronze Cap') end)
assert(#sent==0)
gState.Encumbrance[6]=false;single('Head','Bronze Cap');sent={};gState.Disabled[5]=true
stable(function() gFunc.Equip('Body','Tunic') end)
assert(worn[5]==1 and worn[6]==nil and #sent==0)
''')

 def test_missing_and_fallback_candidates_do_not_leave_stale_masks(self):
  self.run_lua(r'''
items[2].Count=0
stable(function() gFunc.EquipSet({Body='Tunic',Head='Bronze Cap'}) end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
items[2].Count=1;sent={}
gEquip.SetFallback({[5]=gEquip.MakeItemTable('Bronze Cap')})
stable(function() gFunc.EquipSet({Head='Missing hat',Body='Tunic'}) end)
assert(worn[6]==2 and worn[5]==nil and #sent==1)
gEquip.SetFallback(nil)
''')

 def test_rejected_multislot_candidate_cannot_block_compatible_survivors(self):
  self.run_lua(r'''
local s=loadSets({Broad={Body='Full suit'}},{Mine={Head='Bronze Cap',Hands='Gloves',Feet='Boots'}})
stable(function() gFunc.EquipSet(s.Mine);gFunc.EquipSet(s.Broad) end)
assert(worn[5]==1 and worn[7]==5 and worn[9]==7 and worn[6]==nil and #sent==3)
sent={};stable(function() gFunc.Equip('Legs','Trousers');gFunc.Equip('Feet',{Name='Boots',Priority=5}) end)
assert(worn[8]==nil and worn[9]==7 and #sent==0)
''')

 def test_combined_forced_and_delayed_sets_keep_player_provenance(self):
  self.run_lua(r'''
local s=loadSets({Idle={Body='Tunic'}},{Mine={Head='Bronze Cap'}})
local c=gFunc.Combine(s.Idle,s.Mine)
local mixed=gFunc.Combine({[1]='ignored',Unknown='ignored',Head='Bronze Cap'}, {false,Body='Shirt'})
assert(mixed.Head=='Bronze Cap' and mixed.Body=='Shirt' and mixed[1]==nil and mixed.Unknown==nil)
stable(function() gFunc.EquipSet(c) end)
assert(worn[5]==1 and worn[6]==nil and #sent==1)
sent={};gFunc.ForceEquipSet(c);assert(#sent==0)
gEquip.ClearBuffer();gFunc.InterimEquipSet(c);gEquip.ProcessImmediateBuffer('set')
assert(#sent==0 and worn[5]==1)
''')

 def test_weapon_ammo_policy_is_unchanged(self):
  self.run_lua(r'''
-- This armour fix must not install the old blanket Range/Ammo compatibility lock.
gFunc.ForceEquipSet({Range='Dunna',Ammo='Morion Tathlum'})
assert(#sent==2)
''')

if __name__=='__main__':unittest.main(verbosity=2)
