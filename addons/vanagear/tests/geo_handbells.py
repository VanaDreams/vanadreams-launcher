"""Off-game regressions for handbells and saved Indi choices in existing GEO profiles."""
from pathlib import Path
import tempfile
import unittest
from lupa import luajit21
from ranged_priority import PRELUDE, ADDON

GEO = r'''
local underlay = true
core.Underlay = function() return underlay end
vp.IsOurs = function() return true end
function geo(ownText, withBuilt, custom)
    underlay = withBuilt ~= false
    local p = dofile(ADDON_ROOT .. '/templates/GEO.lua') -- the pre-existing handler stays unchanged
    p.FilePath = AshitaCore:GetInstallPath() .. 'GEO.lua'
    p.VgJob = 'GEO'
    if custom then p.VgTemplate = false end
    local built = [[{Idle={Range='Generated range',Ammo='Morion Tathlum',Body='Idle body'},
        Precast={Range='Generated range',Ammo='Morion Tathlum',Body='Precast body'},
        Geomancy={Range='Generated range',Ammo='Morion Tathlum',Body='Geomancy body'},
        IndiFury={Range='Generated range',Ammo='Morion Tathlum',Body='Indi body'},
        GeoFury={Range='Generated range',Ammo='Morion Tathlum',Body='Geo body'},
        Interim={Range='Generated range',Ammo='Morion Tathlum',Body='Interim body'}}]]
    local f=assert(io.open(p.FilePath,'w'))
    f:write('return {Sets=' .. ownText .. ',Built=' .. built .. '}'); f:close()
    gProfile=p; assert(vp.Refresh())
    return p
end
function cast(p, name)
    world.action={Name=name,Skill='Geomancy',CastTime=3000}
    p.HandlePrecast(); p.HandleMidcast()
end
'''

class GeoHandbells(unittest.TestCase):
    def lua(self, test):
        with tempfile.TemporaryDirectory() as tmp:
            lua=luajit21.LuaRuntime(unpack_returned_tuples=True)
            lua.execute(PRELUDE,str(ADDON),tmp+'\\')
            lua.execute(GEO)
            lua.execute(test)

    def test_manual_bell_survives_all_generated_phases(self):
        self.lua(r'''
local p=geo('{}')
current={Range='Dunna'}
for _,name in ipairs({'Geo-Fury','Indi-Fury'}) do
    for _,stage in ipairs({false,true}) do
        gVana.Toggles.Stage=stage
        cast(p,name); p.HandleDefault()
        assert(current.Range=='Dunna' and current.Ammo==nil)
    end
end
assert(next(gState.Disabled)==nil)
''')

    def test_indi_family_applies_in_old_profile_with_underlay_on_or_off(self):
        self.lua(r'''
for _,underlay in ipairs({true,false}) do
    local p=geo("{Indi={Range='Dunna',Body='My Indi body'}}",underlay)
    current={Range='Previous range'};cast(p,'Indi-Fury')
    assert(current.Range=='Dunna' and current.Body=='My Indi body')
    -- No file or saved per-spell picks were written as a side effect of inheritance.
    assert(gVana.FileSets.IndiFury==nil and gVana.FileSets.Indi.Range=='Dunna')
    assert(gVana.Own.IndiFury==nil)
    assert(gVana.SetSources[p.Sets.IndiFury].Body==nil) -- inherited armour is authored, even if its text matches built
    local f=assert(io.open(p.FilePath));local text=f:read('*all');f:close()
    assert(not text:find("IndiFury={Range='Dunna'",1,true))
    current={Range='Previous range'};cast(p,'Geo-Fury');assert(current.Range=='Previous range')
end
''')

    def test_specific_spell_choices_and_remove_override_family(self):
        self.lua(r'''
local p=geo("{Geomancy={Range='Geomancy bell'},Indi={Range='Dunna',Body='Indi body'},IndiFury={Range='Filiae Bell',Hands='My gloves'}}")
current={};cast(p,'Indi-Fury')
assert(current.Range=='Filiae Bell' and current.Body=='Indi body' and current.Hands=='My gloves')
assert(gVana.SetSources[p.Sets.IndiFury].Body==nil and gVana.SetSources[p.Sets.GeoFury].Body=='Geo body')
current={};cast(p,'Geo-Fury');assert(current.Range=='Geomancy bell')
p=geo("{Indi={Range='Dunna'},IndiFury={Range='remove'}}")
current={};cast(p,'Indi-Fury');assert(current.Range=='remove')
''')

    def test_scope_excludes_other_sets_and_imported_custom_profiles(self):
        self.lua(r'''
local p=geo("{Indi={Range='Dunna'},Indi_Precast={Range='Precast bell'},Indi_Self={Range='Self bell'},Indigo={Range='Unrelated'}}")
assert(p.Sets.Indi_Precast.Range=='Precast bell' and p.Sets.Indi_Self.Range=='Self bell')
assert(p.Sets.Indigo.Range=='Unrelated' and p.Sets.GeoFury.Range==nil)
p=geo("{Indi={Range='Dunna'}}",true,true)
current={Range='Custom choice'};cast(p,'Indi-Fury');assert(current.Range=='Custom choice')
p=geo('{Indi={}}');current={Range='Manual bell'};cast(p,'Indi-Fury');assert(current.Range=='Manual bell')
''')

    def test_changes_are_reread_and_no_bell_is_chosen_automatically(self):
        self.lua(r'''
local p=geo("{Indi={Range={Name='Dunna'}}}")
current={};cast(p,'Indi-Fury');assert(current.Range.Name=='Dunna')
p=geo("{Indi={Range='Filiae Bell'}}");current={};cast(p,'Indi-Fury');assert(current.Range=='Filiae Bell')
p=geo('{}');current={};cast(p,'Indi-Fury');assert(current.Range==nil and current.Ammo==nil)
-- An explicit Ammo choice remains intentional, not silently blocked by a handbell lock.
p=geo("{Indi={Range='Dunna'},IndiFury={Ammo='Morion Tathlum'}}")
current={};cast(p,'Indi-Fury');assert(current.Range=='Dunna' and current.Ammo=='Morion Tathlum')
assert(next(gState.Disabled)==nil)
''')

if __name__=='__main__':
    unittest.main(verbosity=2)
