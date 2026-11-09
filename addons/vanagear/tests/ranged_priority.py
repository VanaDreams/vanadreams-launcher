"""Off-game Range/Ammo priority regressions. No client, server or real profile is used."""
from pathlib import Path
import tempfile
import unittest
from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent

PRELUDE = r'''
local root, install = ...
package.path = root .. '/?.lua;' .. package.path
bit = require('bit')
function T(t) return setmetatable(t or {}, { __index = { append = function(self, v) self[#self+1] = v end } }) end
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })
ashita = { fs = { exists = function() return true end, create_directory = function() end } }
package.preload['encoding'] = function() return { ShiftJIS_To_UTF8 = function(_, s) return s end } end
AshitaCore = { GetInstallPath = function() return install end,
    GetChatManager = function() return { QueueCommand = function(_, _, s) queued[#queued+1] = s end } end,
    GetResourceManager = function() return { GetAbilityById = function() return nil end } end,
    GetMemoryManager = function() return { GetPlayer = function() return {} end } end }
gSettings = {}
gState = { Disabled = {}, PlayerName = 'Test', PlayerId = 1 }
world = { player = { Name = 'Test', Status = 'Idle', HPP = 100, MPP = 100, MaxMP = 100,
    TP = 3000, IsMoving = false, MainJob = 'BST' }, buffs = 0 }
current, writes, queued = {}, {}, {}
local function equip(slot, item) current[slot] = item; writes[#writes+1] = {slot, item} end
gFunc = { Equip = equip, ForceEquip = equip,
    EquipSet = function(s) for k,v in pairs(s) do equip(k,v) end end,
    ForceEquipSet = function(s) for k,v in pairs(s) do equip(k,v) end end,
    InterimEquipSet = function(s) for k,v in pairs(s) do equip(k,v) end end,
    SetMidDelay = function() end, CancelAction = function() world.cancelled = true end }
gData = { Constants = { EquipSlotNames = { 'Main','Sub','Range','Ammo','Head','Body','Hands','Legs','Feet','Neck','Waist','Ear1','Ear2','Ring1','Ring2','Back' } },
    GetPlayer = function() return world.player end, GetPet = function() return world.pet end,
    GetPetAction = function() return nil end, GetAction = function() return world.action end,
    GetActionTarget = function() return { Name = 'Mob', Distance = 10 } end,
    GetBuffCount = function() return world.buffs end,
    GetEnvironment = function() return { Area = 'West Ronfaure', Time = 12 } end,
    GetEquipment = function() return {} end }
ADDON_ROOT = root
AshitaCore.GetInstallPath = function() return root .. '/../../' end
core = require('vg.core')
AshitaCore.GetInstallPath = function() return install end
core.SavePrefs = function() end
vp = require('vg.profile')
gVanaStock = require('vg.stock')
gVanaInclude = require('vg.include')
gVana.Carries = function() return false end
function loadJob(job, own)
    local p = dofile(root .. '/templates/' .. job .. '.lua')
    local built = {}
    for _,n in ipairs({'Idle','Idle_Regen','Idle_Refresh','Resting','Movement','PetTP','PetDT','PetIdle','PupMelee',
        'Tp_Default','Dt','TH','Town','Call','CallBeast','BestialLoyalty','Reward','Repair','Maintenance',
        'Preshot','Velocity_Preshot','Preshot_Flurry','Preshot_FlurryII','Midshot','TrueShot','Velocity','DoubleShot',
        'Midshot_Acc','Barrage','TripleShot','QuickDraw','QuickDraw_Acc','FireShot','Ws_Default','LastStand'}) do
        built[n] = { Range = 'Old generated range', Ammo = 'Old generated ammo', Body = 'Built ' .. n }
    end
    local merged, owns = vp.Merge(own or {}, built, true)
    for k,v in pairs(merged) do p.Sets[k] = v end
    gProfile, gVana.Own, gVana.Sets = p, owns, p.Sets
    return p
end
'''

class RangedPriority(unittest.TestCase):
    def run_lua(self, body):
        with tempfile.TemporaryDirectory() as tmp:
            L = luajit21.LuaRuntime(unpack_returned_tuples=True)
            L.execute(PRELUDE, str(ADDON), tmp + '\\')
            L.execute(body)

    def test_stale_built_is_ignored_without_mutating_player_choices(self):
        self.run_lua(r'''
local built = { Idle = { Range='Generated gun', Ammo='Generated ammo', Head='Built hat' },
    Tp_Default = { Ammo='Other ammo', Body='Built armour' } }
local own = { Idle = { Range='Animator', Ammo={Name='Pet Food Zeta'} }, Engaged={Ammo='My bullet'} }
local worn, marked = vp.Merge(own, built, true)
assert(worn.Idle.Range == 'Animator' and worn.Idle.Ammo.Name == 'Pet Food Zeta')
assert(worn.Idle.Head == 'Built hat' and marked.Idle.Range and marked.Idle.Ammo)
assert(worn.Tp_Default.Ammo == 'My bullet' and worn.Engaged == worn.Tp_Default)
assert(built.Idle.Range == 'Generated gun' and built.Idle.Ammo == 'Generated ammo')
assert(vp.Merge({}, built).Idle.Range == nil and vp.Merge({}, built).Idle.Ammo == nil)
assert(vp.Merge(own, built, false).Idle.Head == nil)
assert(vp.Merge(own, built, false).Idle.Range == 'Animator')
''')

    def test_old_profile_refresh_ignores_global_range_but_preserves_data(self):
        self.run_lua(r'''
local path = AshitaCore:GetInstallPath() .. 'profile.lua'
local f = assert(io.open(path, 'w'))
f:write([[return { Sets={ Idle={Range='My bow',Ammo='My arrow'}, Preshot={Range='Action bow'} },
    Built={Idle={Main='Generated sword',Sub='Generated shield',Range='Generated gun',Ammo='Generated bullet',Head='Built hat'},Midshot={Range='Generated gun',Ammo='Generated bullet'}} }]])
f:close()
vp.IsOurs = function() return true end
gProfile = { FilePath=path, VgJob='RNG', Sets={} }
gVana.Prefs.Weapons=false
gVana.Prefs.WeaponPick.RNG={Range='Legacy gun',Main='Saved axe'}
assert(vp.Refresh())
assert(gProfile.Sets.Idle.Range == 'My bow' and gProfile.Sets.Preshot.Range == 'Action bow')
assert(gProfile.Sets.Midshot.Range == nil and gProfile.Sets.Midshot.Ammo == nil)
assert(gProfile.Sets.Idle.Main == 'Saved axe' and gProfile.Sets.Midshot.Main == 'Saved axe')
assert(gVana.Prefs.WeaponPick.RNG.Range == 'Legacy gun')
assert(gVana.Built.Idle.Main == nil and gVana.Built.Idle.Sub == nil)
assert(gVana.Built.Idle.Range == nil and gVana.Built.Idle.Ammo == nil)
gVana.Prefs.Weapons=true; assert(vp.Refresh())
assert(gProfile.Sets.Idle.Main == nil and gProfile.Sets.Idle.Sub == nil)
assert(gVana.Own.Idle.Range and gVana.Own.Idle.Ammo)
core.SetWeaponPick('Range', 'New gun')
assert(current.Range == 'New gun' and gVana.Prefs.WeaponPick.RNG.Range == 'Legacy gun')
core.SetWeaponPick('Range', nil)
assert(current.Range == 'New gun')
''')

    def test_plain_bst_jug_and_food_macros_survive_default_and_actions(self):
        self.run_lua(r'''
local p=loadJob('BST')
for _,ability in ipairs({'Call Beast','Bestial Loyalty','Reward'}) do
    current = { Ammo=(ability=='Reward') and 'Pet Food Zeta' or 'Wormy Broth', Range='Player range' }
    p.HandleDefault()
    world.action={Name=ability,Type='Job Ability'}
    p.HandleAbility()
    p.HandleDefault()
    assert(current.Ammo == ((ability=='Reward') and 'Pet Food Zeta' or 'Wormy Broth'))
    assert(current.Range=='Player range' and current.Body ~= nil)
    -- Last item consumed or failed action: no generated refill or stale held choice.
    current.Ammo=nil; world.action=nil; p.HandleDefault(); assert(current.Ammo==nil)
end
p=loadJob('BST',{CallBeast={Ammo='My action jug'},Reward={Ammo='My action food'}})
world.action={Name='Call Beast',Type='Job Ability'}; p.HandleAbility(); assert(current.Ammo=='My action jug')
world.action={Name='Reward',Type='Job Ability'}; p.HandleAbility(); assert(current.Ammo=='My action food')
assert(gState.Disabled[3]==nil and gState.Disabled[4]==nil and core.HoldByHand==nil)
''')

    def test_rng_cor_phases_preserve_manual_pairs_and_explicit_swaps(self):
        self.run_lua(r'''
for _,job in ipairs({'RNG','COR'}) do
    local p=loadJob(job)
    for _,pair in ipairs({{'Bow','Arrow'},{'Gun','Bullet'},{'Crossbow','Bolt'},{'','Throwing weapon'},
        {'Fishing rod','Bait'},{'Animator','Oil'},{'',''}}) do
        current={Range=pair[1],Ammo=pair[2]}; world.buffs=1
        p.HandleDefault(); p.HandlePreshot(); p.HandleMidshot()
        world.action={Name='Last Stand',Type='Weaponskill'}; p.HandleWeaponskill()
        if job=='COR' then world.action={Name='Fire Shot',Type='Job Ability'}; p.HandleAbility() end
        assert(current.Range==pair[1] and current.Ammo==pair[2], job .. ' changed manual pair')
    end
    p=loadJob(job,{Preshot={Range='New bow',Ammo='New arrow'},Midshot={Ammo='Action arrow'}})
    current={Range='Old gun',Ammo='Old bullet'}
    p.HandlePreshot(); assert(current.Range=='New bow' and current.Ammo=='New arrow')
    p.HandleMidshot(); assert(current.Range=='New bow' and current.Ammo=='Action arrow')
end
''')

    def test_pup_animator_is_free_and_oil_actions_still_work(self):
        self.run_lua(r'''
local p=loadJob('PUP')
for _,ability in ipairs({'Repair','Maintenance'}) do
    gVana.Carries=function(n) return n=='Automat. Oil +2' end
    current={Range='Animator',Ammo='Player ammo'}; p.HandleDefault()
    world.action={Name=ability,Type='Job Ability'}; p.HandleAbility()
    assert(current.Range=='Animator' and current.Ammo=='Automat. Oil +2')
    p.HandleDefault(); assert(current.Range=='Animator' and current.Ammo=='Automat. Oil +2')
    gVana.Carries=function() return false end; current.Ammo=nil
    p.HandleAbility(); p.HandleDefault(); assert(current.Ammo==nil and current.Range=='Animator')
end
''')

    def test_helper_jug_food_and_failed_action_release_unchanged(self):
        self.run_lua(r'''
local use=require('vg.use')
local now=0; os.clock=function() return now end
gVana.Who={JobName='BST',Level=99}
gVana.Pieces={
 {Name='Wormy Broth',Equip='Wormy Broth',Server={slot=8,jobs=256,skill=0,level=1}},
 {Name='Pet Food Zeta',Equip='Pet Food Zeta',Server={slot=8,jobs=256,skill=0,level=1}}}
for _,kind in ipairs({'call','reward'}) do
    if kind=='call' then use.Call('Call Beast','Wormy Broth') else use.Reward('Pet Food Zeta') end
    local chosen=(kind=='call') and 'Wormy Broth' or 'Pet Food Zeta'
    assert(current.Ammo==chosen)
    now=now+1.1; use.Tick('HandleDefault'); assert(#queued>0)
    world.action={ActionType='Ability'}; use.Tick('HandleAbility')
    world.action=nil; current.Ammo=nil; use.Tick('HandleDefault'); assert(current.Ammo==nil)
end
use.Call('Bestial Loyalty','Wormy Broth')
now=now+1.1; use.Tick('HandleDefault')
now=now+3.1; current.Ammo=nil; use.Tick('HandleDefault'); assert(current.Ammo==nil)
use.With('shot',{Range='Gun',Ammo='Bullet'},'/ra <t>')
now=now+1.1; use.Tick('HandleDefault')
now=now+6.1; current.Ammo=nil; use.Tick('HandleDefault'); assert(current.Ammo==nil)
assert(gState.Disabled[3]==nil and gState.Disabled[4]==nil)
''')

    def test_no_profile_fallback_does_not_restore_generated_weapons(self):
        self.run_lua(r'''
vp.WriteBuilt=function() return false end
vp.Refresh=function() return false end
gVana.Fresh={job='BST',sets={Idle={Main='Old sword',Sub='Old shield',Range='Old range',Ammo='Old ammo',Body='Built body'}}}
core.ApplyOwn()
assert(gVana.Sets.Idle.Main==nil and gVana.Sets.Idle.Sub==nil)
assert(gVana.Sets.Idle.Range==nil and gVana.Sets.Idle.Ammo==nil)
assert(gVana.Sets.Idle.Body=='Built body')
assert(gVana.Built.Idle.Range==nil and gVana.Fresh.sets.Idle.Range=='Old range')
''')

    def test_all_player_builds_skip_range_ammo_but_not_armour(self):
        self.run_lua(r'''
AshitaCore.GetInstallPath = function() return ADDON_ROOT .. '/../../' end
local plan=require('vg.plan')
for id,job in ipairs({'WAR','MNK','WHM','BLM','RDM','THF','PLD','DRK','BST','BRD','RNG','SAM','NIN','DRG','SMN','BLU','COR','PUP','DNC','SCH','GEO','RUN'}) do
    for _,weapons in ipairs({false,true}) do
        local sets,held=plan.For({Job=id,JobName=job,Level=99,Sub=0,SubName='',SubLevel=0},{},{Weapons=weapons})
        assert(#sets>0)
        for _,s in ipairs(sets) do
            for _,slot in ipairs(s.Slots) do assert(slot>4,job .. ' generated weapons/ammo') end
        end
        assert(held[3]==nil and held[4]==nil) -- UI must still offer normal per-set choices.
    end
end
''')

if __name__ == '__main__':
    unittest.main(verbosity=2)
