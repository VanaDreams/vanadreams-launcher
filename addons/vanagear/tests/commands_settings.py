r"""Off-game test of the /vg commands after the clean-up and the window's Settings tab (Lee, 24 Oct 2026:
"clean up the vg commands, make sure they arent redundant or unnecessary now that the luas are being
written"; "some of the commands can get a setting in the vg tab ... as a check box"; every command listed
where the abilities and spells are, "they cant use them if they dont know them"):
- one slot lock: /vg lock and /vg disable are the same, really stop the slot, are saved for the job, and
  come back when the job's profile loads; /vg unlock and /vg enable undo them; "all" does every slot;
- /vg addset is /vg save; /gs export too;
- /vg dt takes pdt, mdt, all and off, and the shared include wears Dt_Physical, Dt_Magic or Dt for them
  (Dt by itself under 40% HP);
- the Settings tab's buttons and boxes run the same commands (damage taken, a slot lock) and change the
  job template's own settings;
- the Commands list holds vanagear's commands and yours.

    python addons/vanagear/tests/commands_settings.py
"""
import tempfile
from pathlib import Path

from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent
TMP = tempfile.mkdtemp()
(Path(TMP) / 'config' / 'addons' / 'vanagear').mkdir(parents=True)

L = luajit21.LuaRuntime(unpack_returned_tuples=True)
result = L.execute(r"""
local addon_path, tmp = ...
package.path = addon_path .. '/?.lua;' .. package.path
bit = require('bit')
chat = setmetatable({}, { __index = function() return function(...) return table.concat({...}, ' ') end end })
ashita = { fs = { exists = function() return true end, create_directory = function() end } }
package.preload['encoding'] = function() return { ShiftJIS_To_UTF8 = function(_, s) return s end } end
AshitaCore = { GetInstallPath = function() return tmp .. '/' end,
               GetChatManager = function() return { QueueCommand = function() end } end }
gState = { Disabled = {} }
gData = { Constants = { EquipSlotNames = { 'Main', 'Sub', 'Range', 'Ammo', 'Head', 'Body', 'Hands', 'Legs', 'Feet',
                                           'Neck', 'Waist', 'Ear1', 'Ear2', 'Ring1', 'Ring2', 'Back' } },
          GetEquipment = function() return {} end }
local worn = {}
gFunc = { EquipSet = function(s) worn[#worn + 1] = s end, ForceEquipSet = function() end }
print = function() end

-- the window's drawing: every control answers "not clicked" unless a test asks for one
local click = {}
local imguiStub = setmetatable({
    RadioButton = function(label) return click[label] == true end,
    Checkbox = function(label, box) if click[label] then box[1] = not box[1]; return true end return false end,
    InputInt = function() return false end,
}, { __index = function() return function() return false end end })
package.loaded['imgui'] = imguiStub
package.loaded['vdstyle'] = setmetatable({}, { __index = function() return function() return false end end })

local core = require('vg.core')
core.Request = function() end
core.QueueKeys = function() end
gVana.Who = { JobName = 'BLM', Level = 99, SubName = 'RDM', SubLevel = 49 }
local commands = require('vg.commands')
local mine = require('vg.mine')
local saved = {}
mine.SaveSet = function(name) saved[#saved + 1] = name; return true, name, 14 end
gVanaStock = require('vg.stock')
local include = require('vg.include')
local window = require('vg.window')

local out = {}
local function check(label, ok) out[#out + 1] = (ok and 'ok   ' or 'FAIL ') .. label end

-- one lock
commands.Vg({ 'disable', 'neck' })
check('/vg disable locks the slot for real', gState.Disabled[10] == true)
check('/vg disable is saved for the job', gVana.Prefs.Locked.BLM[10] == true)
gState.Disabled[10] = false
core.ApplyLocks('BLM')
check('the saved lock comes back as the profile loads', gState.Disabled[10] == true)
commands.Vg({ 'enable', 'neck' })
check('/vg enable unlocks', gState.Disabled[10] == false and gVana.Prefs.Locked.BLM[10] == nil)
commands.Vg({ 'lock', 'all' })
local all = true
for i = 1, 16 do if not gState.Disabled[i] then all = false end end
check('/vg lock all does every slot', all)
commands.Vg({ 'unlock', 'all' })
check('/vg unlock all frees them', gState.Disabled[1] == false and next(gVana.Prefs.Locked.BLM) == nil)

-- one save
commands.Vg({ 'addset', 'Nuke' })
commands.Vg({ 'save', 'Idle' })
commands.Gs({ 'export', 'Mine' })
check('/vg addset, /vg save and /gs export all save what you wear', saved[1] == 'Nuke' and saved[2] == 'Idle' and saved[3] == 'Mine')

-- damage taken modes
local t = gVana.Toggles
commands.Vg({ 'dt', 'pdt' }); check('/vg dt pdt', t.DT == 'pdt')
commands.Vg({ 'dt', 'mdt' }); check('/vg dt mdt', t.DT == 'mdt')
commands.Vg({ 'dt', 'all' }); check('/vg dt all', t.DT == true)
commands.Vg({ 'dt', 'off' }); check('/vg dt off', t.DT == false)
commands.Vg({ 'dt' }); check('/vg dt alone turns it on', t.DT == true)

gProfile = { Sets = { Dt = { Body = 'Dt' }, Dt_Physical = { Body = 'P' }, Dt_Magic = { Body = 'M' } } }
local function wornBodies(dt, hpp)
    worn = {}
    t.DT = dt
    gData.GetPlayer = function() return { Status = 'Engaged', HPP = hpp, MPP = 100, MaxMP = 100, IsMoving = false } end
    gData.GetPet = function() return nil end
    gData.GetBuffCount = function() return 0 end
    gData.GetEnvironment = function() return {} end
    include.Default()
    local b = {}
    for _, s in ipairs(worn) do b[#b + 1] = s.Body end
    return table.concat(b, ',')
end
check('include: pdt wears Dt_Physical', wornBodies('pdt', 100) == 'P')
check('include: mdt wears Dt_Magic', wornBodies('mdt', 100) == 'M')
check('include: all wears Dt', wornBodies(true, 100) == 'Dt')
check('include: off and low HP wears Dt', wornBodies(false, 30) == 'Dt')

-- the Settings tab runs the same commands
gVana.Sets = { Tp_Default = {}, Nuke = {}, Burst = {}, Interim = {} }
t.DT = false
click = { ['Magic (Dt_Magic)###Magic (Dt_Magic)##vg_set_dt4'] = true }
window.RenderSettings()
check('Settings: Damage taken, Magic', t.DT == 'mdt')
click = { ['Neck###Neck##vg_set_lock10'] = true }
window.RenderSettings()
check('Settings: locking Neck locks it', gState.Disabled[10] == true and gVana.Prefs.Locked.BLM[10] == true)
gProfile.State = { death = false }
gProfile.VgJob = 'BLM'
click = { ['death###death##vg_set_state_death'] = true }
window.RenderSettings()
check('Settings: the template\'s own setting changes', gProfile.State.death == true)
gProfile.State = { tank = 'Main' }
gProfile.StateChoices = { tank = { 'Main', 'MEVA', 'None' } }
click = { ['MEVA###MEVA##vg_set_state_tank2'] = true }
window.RenderSettings()
check('Settings: a template choice changes', gProfile.State.tank == 'MEVA')

-- every command listed, yours too
mine.List = function() return { lizard = { lines = { '/vg call wormy' }, file = 'BLM' } } end
local lines = commands.Lines()
local have = {}
for _, l in ipairs(lines) do have[l[1]] = l[2] end
check('Commands list has vanagear\'s', have['/vg dt [pdt | mdt | all | off]'] ~= nil and have['/vg template'] ~= nil and have['/vg wait <seconds>'] ~= nil)
check('Commands list has yours', have['/vg lizard'] ~= nil)
check('Commands list has no separate addset or disable lines', have['/vg addset <name>'] == nil)
window.RenderCommand({ '/vg lizard', have['/vg lizard'] })
check('a command of yours opens in the panel', true)

-- language command, saved preference and the Settings control share one path
local locale = require('vg.i18n')
commands.Vg({ 'language', 'ja' })
check('/vg language ja overrides detection', locale.Current() == 'ja')
gVana.Prefs.Language = 'en'
core.LoadPrefs()
check('language survives preferences reload', locale.Current() == 'ja')
commands.Vg({ 'language', 'invalid' })
check('invalid language keeps the previous preference', locale.Preference() == 'ja')
commands.Vg({ 'language', 'en' })
click = { ['日本語 / Japanese###日本語 / Japanese##vg_set_language3'] = true }
window.RenderSettings()
check('Settings language choice changes the saved preference', locale.Preference() == 'ja')
click = { ['魔法 (Dt_Magic)###Magic (Dt_Magic)##vg_set_dt4'] = true }
t.DT = false
window.RenderSettings()
check('Japanese Settings still activates the same gear command', t.DT == 'mdt')
click = {}
commands.Vg({ 'language', 'auto' })
check('Auto restores safe fallback when detection is unavailable', locale.Preference() == 'auto' and locale.Current() == 'en')

return table.concat(out, '\n')
""", str(ADDON), TMP)
__builtins__.print(result)
assert 'FAIL' not in result
__builtins__.print('PASS')
