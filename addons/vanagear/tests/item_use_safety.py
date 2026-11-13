"""Synthetic item-use regression tests; these do not prove client crash freedom.

Runs real itemguard/use/func/equip Lua using the existing simulated server harness.
Run: python addons/vanagear/tests/item_use_safety.py
"""
import unittest
from lupa import luajit21
from armor_conflicts import ADDON, LUA

SETUP = r'''
now=0; os.clock=function() return now end
package.loaded['vg.data']={Items=function() return {} end, SlotBit={}}
guard=require('vg.itemguard')
gVana.Prefs.Weapons=true
commands={}
AshitaCore.GetChatManager=function() return {QueueCommand=function(_,_,s) commands[#commands+1]=s end} end
gData.GetAction=function() return gState.PlayerAction end
function piece(index,slot)
 local i=items[index]
 return {Id=i.Id,Bag=0,Index=index,Name=res[i.Id].Name[1],Equip=res[i.Id].Name[1],
  Server={slot=2^(slot-1),use={delay=8,act=8}}}
end
function begin(slot,index)
 worn[slot]=index
 return guard.Begin(0,index,items[index],8,99)
end
'''


class ItemUseSafety(unittest.TestCase):
    def run_lua(self, test):
        lua = luajit21.LuaRuntime(unpack_returned_tuples=True)
        lua.execute(LUA, str(ADDON))
        lua.execute(SETUP)
        lua.execute(test)

    def test_equipping_usable_items_without_using_them_does_not_lock(self):
        self.run_lua(r'''
local usable=piece(1,5);gVana.Pieces={usable}
package.loaded['vg.data'].Items=function() return {[usable.Id]=usable.Server} end
gFunc.ForceEquip('Head',usable.Name)
assert(worn[5]==1 and guard.Get(5)==nil)
gFunc.ForceEquipSet({Head=usable.Name,Neck='Necklace'})
assert(guard.Get(5)==nil and guard.Get(10)==nil)
gEquip.UnequipSlot(5);assert(worn[5]==nil)
gFunc.ForceEquip('Head',usable.Name)
gFunc.ForceEquip('Head','Head and hands')
assert(worn[5]==4 and guard.Get(5)==nil)
''')

    def test_nonusable_item_attempt_creates_no_hold(self):
        self.run_lua(r'''
worn[5]=1
assert(guard.Begin(0,1,items[1],0,99)==nil)
assert(guard.Get(5)==nil)
gEquip.UnequipSlot(5);assert(worn[5]==nil)
-- Server metadata still recognizes a usable piece when client cast time is zero.
worn[5]=1
package.loaded['vg.data'].Items=function() return {[items[1].Id]={use={act=8,delay=8}}} end
assert(guard.Begin(0,1,items[1],0,99)~=nil)
''')

    def test_direct_item_packet_installs_hold_before_profile_callback(self):
        self.run_lua(r'''
worn[5]=1;res[items[1].Id].CastTime=32
gSettings.ItemBase=0;gSettings.ItemOffset=1
struct={unpack=function(_,_,offset)
 if offset==15 then return 1 end -- inventory index
 if offset==17 then return 0 end -- inventory bag
 if offset==13 then return 7 end -- target index
 if offset==5 then return 99 end -- target server id
 error('Unexpected outgoing item field '..offset)
end}
local callback=false;local injected=false
gState.HandleEquipEvent=function(name)
 assert(name=='HandleItem')
 local h=guard.Get(5)
 assert(h~=nil and h.id==items[1].Id and h.phase=='sent','profile ran before protection')
 assert(h.cast==8,'item cast should exclude unrelated action offset')
 callback=true;gFunc.ForceEquip('Head','remove')
 assert(worn[5]==1 and #sent==0)
end
gState.Inject=function(id,p)
 assert(callback and id==0x37 and guard.Get(5)~=nil)
 injected=true
end
local handlers=require('packethandlers')
handlers.HandleItemPacket({totable=function() return {} end},false)
assert(callback and injected and guard.Get(5)~=nil)
''')

    def test_real_packet_dispatch_ignores_unrelated_and_cancels_zone(self):
        self.run_lua(r'''
local p=piece(1,15);p.Server.slot=16384;res[p.Id].Slots=16384
gVana.Pieces={p};package.loaded['vg.lists']={UseWords={[p.Name]='test'}}
gVanaUse=require('vg.use');gVanaUse.Start('test')
local h=guard.Begin(0,1,items[1],8,99)
gState.PlayerId=123
gData.Constants.ActionCompleteTypes={contains=function(_,n) return n==1 or n==5 end}
struct={unpack=function(_,d) return d.actor end}
ashita={bits={unpack_be=function(d,offset,start,width)
 if offset==10 and start==2 then return d.category end
 if offset==10 and start==6 then return d.parameter end
 if offset==18 then return d.target end
 if offset==26 then return p.Id end
 error('unexpected packet field')
end}}
gVanaCore={ZoneOut=function() end}
gPacketHandlers=require('packethandlers')
local function incoming(category,parameter,target)
 local d={actor=123,category=category,parameter=parameter,target=target}
 gPacketHandlers.HandleIncoming0x28({data=d,data_raw=d})
end
gState.PlayerAction={};incoming(1,0,99)
assert(gState.PlayerAction==nil and guard.Active(h))
incoming(8,28787,99);assert(h.phase=='sent')
now=7;incoming(9,0,99);assert(h.phase=='active')
now=15;incoming(5,p.Id,99);assert(h.phase=='settle')
now=16.99;assert(guard.Active(h))
gPacketHandlers.HandleIncomingPacket({id=0x00B})
assert(not guard.Active(h))
now=100;gVanaUse.Tick();assert(#commands==0,'zone left a stale helper command')
''')

    def test_finish_identity_and_two_second_grace(self):
        self.run_lua(r'''
local h=begin(5,1)
now=8;guard.Finished(items[1].Id+1,99);assert(guard.Active(h))
guard.Finished(items[1].Id,100);assert(h.phase=='sent')
guard.Finished(items[1].Id,99);assert(h.phase=='settle')
now=9.99;assert(guard.Active(h))
guard.Finished(items[1].Id,99) -- duplicate must not restart grace
now=10;assert(not guard.Active(h))
''')

    def test_packet_field_offsets_and_widths_with_packed_bit_model(self):
        # Models the bitstream contract; does not execute Ashita's native decoder.
        self.run_lua(r'''
gState.PlayerId=123
gData.Constants.ActionCompleteTypes={contains=function(_,n) return n==5 end}
struct={unpack=function(_,d,offset)
 local n=0;for i=0,3 do n=n+d:byte(offset+i)*2^(8*i) end;return n
end}
ashita={bits={unpack_be=function(d,offset,start,width)
 local n=0
 for i=0,width-1 do
  local at=offset*8+start+i
  n=n*2+math.floor(d:byte(math.floor(at/8)+1)/2^(7-at%8))%2
 end
 return n
end}}
gPacketHandlers=require('packethandlers')
local function packet(category,parameter,target,item)
 local b={};for i=1,40 do b[i]=0 end;b[6]=123
 local function put(at,width,n)
  for i=0,width-1 do
   local pos=at+i;local digit=math.floor(n/2^(width-1-i))%2
   b[math.floor(pos/8)+1]=b[math.floor(pos/8)+1]+digit*2^(7-pos%8)
  end
 end
 put(82,4,category);put(86,32,parameter);put(150,32,target);put(213,17,item or 0)
 local d=string.char(unpack(b));gPacketHandlers.HandleIncoming0x28({data=d,data_raw=d})
end
local h=begin(5,1)
packet(9,0,99,items[1].Id+1);assert(h.phase=='sent')
packet(9,0,99,items[1].Id);assert(h.phase=='active')
packet(5,65536+items[1].Id,99);assert(h.phase=='active','finish param truncated')
now=8;packet(5,items[1].Id,99);assert(h.phase=='settle' and h.untilAt==10)
guard.Reset();h=begin(5,1)
packet(9,0x74697073,99);assert(h.phase=='settle','32-bit interruption not decoded')
''')

    def test_delayed_start_refresh_and_duplicates_are_bounded(self):
        self.run_lua(r'''
local h=begin(5,1);local first=h.untilAt
now=first-1;guard.Started(items[1].Id,100);assert(h.untilAt==first)
guard.Started(items[1].Id,99);assert(h.untilAt>first)
local deadline=h.untilAt
for i=1,10 do now=now+0.01;guard.Started(items[1].Id,99);guard.Begin(0,1,items[1],8,99) end
assert(h.untilAt==deadline)
now=deadline;assert(not guard.Active(h))
''')

    def test_missing_rejected_interrupted_recover(self):
        self.run_lua(r'''
local h=begin(5,1);now=h.untilAt;assert(not guard.Active(h))
h=guard.Prepare(5,piece(1,5));assert(guard.Active(h))
now=h.untilAt;assert(not guard.Active(h))
h=begin(5,1);guard.Interrupted(100);assert(h.phase=='sent')
guard.Interrupted(99);now=now+1.99;assert(guard.Active(h))
now=now+0.01;assert(not guard.Active(h))
assert(guard.Begin(0,9,items[9],8,99)==nil) -- unequipped consumable
''')

    def test_every_equipment_slot_and_copy_identity(self):
        self.run_lua(r'''
for slot=1,16 do
 guard.Reset();worn={};local h=begin(slot,1)
 assert(h.slot==slot and guard.Allows(slot,0,1))
 assert(not guard.Allows(slot,0,2))
 assert(not guard.Allows(slot,1,1))
 assert(not guard.Allows((slot%16)+1,0,1))
 assert(guard.Allows(((slot+7)%16)+1,1,1))
end
''')

    def test_force_remove_fallback_and_all_packet_styles(self):
        self.run_lua(r'''
for _,style in ipairs({'single','set','auto'}) do
 guard.Reset();worn={};sent={};local h=begin(5,1)
 gEquip.UnequipSlot(5)
 gFunc.ForceEquip('Head','remove')
 gFunc.ForceEquipSet({Head='Head and hands'})
 gEquip.SetFallback({[5]=gEquip.MakeItemTable('Head and hands')})
 gEquip.ClearBuffer();gFunc.Equip('Head','Missing hat');gEquip.ProcessBuffer(style)
 assert(#sent==0 and worn[5]==1,style)
 gEquip.SetFallback(nil)
 guard.Finished(items[1].Id,99);now=now+2
 gEquip.UnequipSlot(5);assert(worn[5]==nil and #sent==1)
 now=now+1
end
''')

    def test_paired_weapon_and_ammo_changes_are_blocked(self):
        self.run_lua(r'''
for _,pair in ipairs({{1,2},{2,1},{3,4},{4,3}}) do
 guard.Reset();worn={};sent={};now=now+1
 local held,other=pair[1],pair[2]
 res[items[1].Id].Slots=2^(held-1)
 res[items[3].Id].Slots=2^(other-1)
 local h=begin(held,1)
 assert(not guard.Allows(other,0,3))
 gFunc.ForceEquip(gData.Constants.EquipSlotNames[other],'Shirt')
 assert(worn[held]==1 and worn[other]==nil and #sent==0)
 worn[other]=3;gEquip.UnequipSlot(other)
 assert(worn[held]==1 and worn[other]==3 and #sent==0,'paired UnequipSlot bypass')
end
''')

    def test_forced_and_delayed_buffers_cannot_bypass_hold(self):
        self.run_lua(r'''
local h=begin(5,1)
gState.ForceSet={Head='Head and hands'};gState.ForceSetTimer=100
gEquip.ProcessBuffer('set');gEquip.ProcessImmediateBuffer('single')
assert(worn[5]==1 and #sent==0)
gState.ForceSet=nil
gEquip.ClearBuffer();gFunc.InterimEquip('Head','Head and hands')
gEquip.ProcessImmediateBuffer('single')
assert(worn[5]==1 and #sent==0)
''')

    def test_moving_held_copy_and_implicit_armor_removal(self):
        self.run_lua(r'''
res[items[1].Id].Slots=16+8192+16384
local h=begin(14,1)
gFunc.ForceEquip('Ring2','Bronze Cap')
assert(worn[14]==1 and worn[15]==nil and #sent==0)
guard.Reset();worn={};h=begin(5,1)
gFunc.ForceEquip('Body','Tunic')
assert(worn[5]==1 and worn[6]==nil and #sent==0)
gFunc.ForceEquip('Neck','Necklace')
assert(worn[10]==9 and worn[5]==1 and #sent==1)
''')

    def test_helper_equip_delay_separate_activation_and_finish(self):
        self.run_lua(r'''
local p=piece(1,15);p.Server.slot=16384;res[p.Id].Slots=16384
gVana.Pieces={p};package.loaded['vg.lists']={UseWords={[p.Name]='test'}}
use=require('vg.use');assert(use.Start('test'));assert(worn[15]==1)
now=7.99;use.Tick();assert(#commands==0)
now=8;use.Tick();assert(#commands==0) -- eight-second equip delay plus safety
now=10;use.Tick();assert(#commands==1)
local h=guard.Begin(0,1,items[1],8,99);guard.Started(items[1].Id,99)
gState.PlayerAction=nil -- unrelated completion / estimated action timeout
now=17.99;use.Tick();assert(guard.Active(h) and worn[15]==1)
now=18;guard.Finished(p.Id,99);use.Tick();assert(guard.Active(h))
now=19.99;use.Tick();assert(guard.Active(h))
now=20;use.Tick();assert(not guard.Active(h))
gEquip.UnequipSlot(15);assert(worn[15]==nil)
''')

    def test_with_item_waits_equip_delay_and_retains_guard(self):
        self.run_lua(r'''
local p=piece(1,15);p.Server.slot=16384;res[p.Id].Slots=16384
gVana.Pieces={p};package.loaded['vg.lists']={UseWords={}}
use=require('vg.use');use.With('ring',{Ring2=p.Name},'/item "Bronze Cap" <me>')
local h=guard.Get(15);assert(h~=nil and worn[15]==1)
now=9.99;use.Tick();assert(#commands==0)
now=10;use.Tick();assert(#commands==1)
guard.Begin(0,1,items[1],8,99)
now=17;use.Tick();assert(guard.Active(h))
now=18;guard.Finished(p.Id,99);now=20;use.Tick();assert(not guard.Active(h))
''')

    def test_with_item_finds_equipped_second_identical_copy(self):
        self.run_lua(r'''
items[3].Id=items[1].Id
local p=piece(1,15);local p2=piece(3,15)
p.Server.slot=16384;p2.Server.slot=16384;res[p.Id].Slots=16384
gVana.Pieces={p,p2};worn[15]=3
package.loaded['vg.lists']={UseWords={}}
use=require('vg.use');use.With('ring',{Ring2=p.Name},'/item "Bronze Cap" <me>')
local h=guard.Get(15);assert(h~=nil and h.index==3 and worn[15]==3)
now=9.99;use.Tick();assert(#commands==0)
now=10;use.Tick();assert(#commands==1)
''')


if __name__ == '__main__':
    unittest.main(verbosity=2)

