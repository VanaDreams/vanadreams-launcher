--[[
    vanagear template: Beastmaster. vanagear writes your BST profile from this the first time you load BST.

    The file is in sections, in the order a profile runs, so each thing has an obvious place:
      1. built       the sets vanagear builds from your bags (it rewrites this; leave it alone)
      2. sets        your sets: the /vg window's drop-downs, "Save what I'm wearing to this set" and /vg addset write here
      3. lists       names your own rules look things up in
      4. state       your toggles and modes
      5. commands    /vg <word> lines, made in the window's Your commands tab
      6. your code   functions of your own: chains, timers, anything
      7. handlers    what goes on, when; each has a place marked for your own lines

    What a Beastmaster does decides which sections are filled (the Vanadreams wiki's Beastmaster guide): it
    fights with a one-handed axe beside a pet, a jug pet from Call Beast or a monster from Charm, and orders
    its moves with Ready (and Sic for a charmed one). The pet comes first in HandleDefault:
    - A pet's move, while it goes off: the move's own set if you made one, else by its kind: PetMab for a
      magical move, PetAcc for one that has to land (sleep, paralyze, silence...) or with /vg pet acc, else
      PetAtk. The moves of each kind are in lists below; change them there.
    - While only the pet fights: PetTP, or PetDT with /vg pet dt.
    - Pressing Ready or Sic: ReadyPrecast (Ready recast down). Call Beast and Bestial Loyalty: Call (the
      pet's level). Reward: Reward (the food goes in the ammo slot; /vg reward picks it). Charm: Charm.
    It casts nothing of its own; its subjob can bring Utsusemi on /NIN, waltzes on /DNC, Haste on /RDM.
]]--

local profile = {};
profile.VgTemplate = true;   -- written from a vanagear job template: vanagear's stock rules stay out of it

-- 1. built --------------------------------------------------------------------------------------------
-- vanagear's sets from your bags: one for standing, fighting, each job ability, weapon skill and spell
-- you have at your level. Rewritten at each rebuild; change gear in sets below, never here.
local built = {
};
profile.Built = built;

-- 2. sets ---------------------------------------------------------------------------------------------
-- Yours. A set named like a built one fills over it slot by slot; a slot you leave out stays built.
-- A set named after a job ability, weapon skill or spell goes on for it (Berserk, SavageBlade, Utsusemi).
local sets = {
    -- sets vanagear does not build, here so they show in the /vg window to fill:
    Stoneskin_Precast = {}, -- over Precast for Stoneskin
    Ws_Acc = {},            -- over a weapon skill with /vg tp acc
    Tp_Proc = {},           -- fighting with /vg tp proc: low damage, for a weakness you have to trigger (Vagary)
    Ws_Proc = {},           -- over a weapon skill with /vg tp proc
    Sleeping = {},          -- while you are asleep
    Reraise = {},           -- while you are weakened
};
profile.Sets = sets;

-- 3. lists --------------------------------------------------------------------------------------------
-- Names your rules check against: weapon skills that want a different set, items, zones.
local lists = {
    -- pet moves that use the pet's magic stats (PetMab)
    magicReady = {
        ['Cursed Sphere'] = true, ['Venom'] = true, ['Toxic Spit'] = true, ['Venom Spray'] = true, ['Bubble Shower'] = true,
        ['Fireball'] = true, ['Plague Breath'] = true, ['Snow Cloud'] = true, ['Acid Spray'] = true, ['Silence Gas'] = true,
        ['Dark Spore'] = true, ['Charged Whisker'] = true, ['Purulent Ooze'] = true, ['Aqua Breath'] = true,
        ['Stink Bomb'] = true, ['Nectarous Deluge'] = true, ['Nepenthic Plunge'] = true, ['Foul Waters'] = true,
        ['Dust Cloud'] = true, ['Sheep Song'] = true, ['Scream'] = true, ['Dream Flower'] = true, ['Roar'] = true,
        ['Gloeosuccus'] = true, ['Palsy Pollen'] = true, ['Soporific'] = true, ['Geist Wall'] = true,
        ['Numbing Noise'] = true, ['Spoil'] = true, ['Hi-Freq Field'] = true, ['Sandpit'] = true, ['Sandblast'] = true,
        ['Filamented Hold'] = true, ['Spore'] = true, ['Infrasonics'] = true, ['Chaotic Eye'] = true, ['Blaster'] = true,
        ['Intimidate'] = true, ['Noisome Powder'] = true, ['TP Drainkiss'] = true, ['Jettatura'] = true,
        ['Spider Web'] = true, ['Corrosive Ooze'] = true, ['Molting Plumage'] = true, ['Swooping Frenzy'] = true,
        ['Pestilent Plume'] = true, ['Gloom Spray'] = true,
    },
    -- pet moves that only have to land (PetAcc)
    accuracyReady = {
        ['Sheep Song'] = true, ['Soporific'] = true, ['Dream Flower'] = true, ['Spore'] = true, ['Palsy Pollen'] = true,
        ['Filamented Hold'] = true, ['Spider Web'] = true, ['Chaotic Eye'] = true, ['Numbing Noise'] = true,
        ['Infrasonics'] = true, ['Scream'] = true, ['Roar'] = true, ['Jettatura'] = true, ['Intimidate'] = true,
        ['Silence Gas'] = true, ['Sandpit'] = true, ['Gloeosuccus'] = true,
    },
};

-- 4. state --------------------------------------------------------------------------------------------
-- Your toggles and modes. /vg dt, /vg kite, /vg th and /vg tp are vanagear's and need nothing here.
local state = {
    fight = true,       -- /vg fwd fight: send the pet in (Fight) when you engage and it has not
    lastFight = 0,      -- when Fight was last sent, so it goes at most every 3 seconds
};
profile.State = state;   -- the /vg window's Settings tab shows these: a true/false as a checkbox, a choice as buttons

-- 5. commands -----------------------------------------------------------------------------------------
-- Your commands: /vg <word> runs the lines in order. "wait 2" pauses two seconds, "then /ws ..." goes
-- when the action before it has finished (a skillchain), "burst /ma ..." the moment it has.
local commands = {
};
profile.Commands = commands;

profile.Packer = {
};

-- 6. your code ----------------------------------------------------------------------------------------
-- Functions of your own go here, above the handlers that call them.

local vg = gVanaInclude;   -- vanagear's checks and toggles, the same for every job

-- a set by name, put on only if you have one by that name
local function wear(name)
    if (sets[name] ~= nil) then gFunc.EquipSet(sets[name]); end
end

-- the set named after an action (Blood Rage -> sets.BloodRage), if there is one
local function setFor(name)
    return sets[string.gsub(name or '', '[^%w]', '')];
end

-- each kind of magic's own set, worn under a spell's own set (whatever job brings the spell)
local skillSets = {
    ['Healing Magic'] = 'Cure', ['Enhancing Magic'] = 'Enhancing', ['Enfeebling Magic'] = 'Enfeebling',
    ['Elemental Magic'] = 'Nuke', ['Dark Magic'] = 'Dark', ['Divine Magic'] = 'Divine', ['Ninjutsu'] = 'Ninjutsu',
    ['Singing'] = 'Song', ['Blue Magic'] = 'BlueMagical', ['Geomancy'] = 'Geomancy',
};

-- vanagear's helpers, the same in every job -------------------------------------------------------------

-- a set you have put gear in yourself (the /vg window, Save what I'm wearing to this set, /vg addset)
local function yours(name)
    return (gVana.Own ~= nil) and (gVana.Own[name] ~= nil) and (next(gVana.Own[name]) ~= nil);
end

-- spells that share a set by name: Regen II wears Regen, Aspir III wears Drain (any job's spells)
local families = {
    { skill = 'Enhancing Magic', match = '^Phalanx', set = 'Phalanx' },
    { skill = 'Enhancing Magic', match = '^Stoneskin', set = 'Stoneskin' },
    { skill = 'Enhancing Magic', match = '^Regen', set = 'Regen' },
    { skill = 'Enhancing Magic', match = '^Refresh', set = 'Refresh' },
    { skill = 'Healing Magic', match = '^Cursna', set = 'Cursna' },
    { skill = 'Dark Magic', match = '^Drain', set = 'Drain' },
    { skill = 'Dark Magic', match = '^Aspir', set = 'Drain' },
    { skill = 'Elemental Magic', match = 'helix', set = 'Helix' },
    -- songs by kind (Victory March wears March, Foe Lullaby II wears Lullaby)
    { skill = 'Singing', match = 'March', set = 'March' }, { skill = 'Singing', match = 'Minuet', set = 'Minuet' },
    { skill = 'Singing', match = 'Madrigal', set = 'Madrigal' }, { skill = 'Singing', match = 'Ballad', set = 'Ballad' },
    { skill = 'Singing', match = 'Paeon', set = 'Paeon' }, { skill = 'Singing', match = 'Minne', set = 'Minne' },
    { skill = 'Singing', match = 'Scherzo', set = 'Scherzo' }, { skill = 'Singing', match = 'Prelude', set = 'Prelude' },
    { skill = 'Singing', match = 'Etude', set = 'Etude' }, { skill = 'Singing', match = 'Carol', set = 'Carol' },
    { skill = 'Singing', match = 'Mazurka', set = 'Mazurka' }, { skill = 'Singing', match = 'Hymnus', set = 'Hymnus' },
    { skill = 'Singing', match = 'Lullaby', set = 'Lullaby' }, { skill = 'Singing', match = 'Elegy', set = 'Elegy' },
    { skill = 'Singing', match = 'Requiem', set = 'Requiem' }, { skill = 'Singing', match = 'Threnody', set = 'Threnody' },
    { skill = 'Singing', match = 'Finale', set = 'Finale' },
};
local function familyOf(spell)
    for _, f in ipairs(families) do
        if (spell.Skill == f.skill) and (string.find(spell.Name, f.match)) then return f.set; end
    end
    return nil;
end

-- enhancing spells whose strength comes from enhancing skill (Bar spells, Temper, Gain-, enspells)
local function bySkill(name)
    return (string.find(name, '^Bar') ~= nil) or (string.find(name, '^Temper') ~= nil) or (string.find(name, '^Gain%-') ~= nil)
        or (string.find(name, '^En%a+$') ~= nil) or (string.find(name, '^En%a+ II$') ~= nil);
end

-- cancels: a Stoneskin or Sneak already on you blocks the new one, so it comes off and the spell goes
-- again a moment later; Sneak comes off before Spectral Jig the same way
local function cancels(action)
    local target = gData.GetActionTarget();
    local onMe = (target ~= nil) and (target.Name == gData.GetPlayer().Name);
    local chat = AshitaCore:GetChatManager();
    local function again(line, delay) (function() chat:QueueCommand(1, line); end):once(delay); end
    if (action.Name == 'Spectral Jig') and (gData.GetBuffCount('Sneak') > 0) then
        gFunc.CancelAction(); chat:QueueCommand(1, '/cancel Sneak'); again('/ja "Spectral Jig" <me>', 2);
    elseif (action.Name == 'Sneak') and onMe and (gData.GetBuffCount('Sneak') > 0) then
        gFunc.CancelAction(); chat:QueueCommand(1, '/cancel Sneak'); again('/ma "Sneak" <me>', 1);
    elseif (action.Name == 'Stoneskin') and (gData.GetBuffCount('Stoneskin') > 0) then
        gFunc.CancelAction(); chat:QueueCommand(1, '/cancel Stoneskin'); again('/ma "Stoneskin" <me>', 1);
    end
end

-- weapon skills that reach from a distance (ranged and magical); every other one needs you close
local distant = {
    ['Flaming Arrow'] = true, ['Piercing Arrow'] = true, ['Dulling Arrow'] = true, ['Sidewinder'] = true,
    ['Blast Arrow'] = true, ['Arching Arrow'] = true, ['Empyreal Arrow'] = true, ['Refulgent Arrow'] = true,
    ['Apex Arrow'] = true, ['Namas Arrow'] = true, ['Jishnu\'s Radiance'] = true, ['Hot Shot'] = true,
    ['Split Shot'] = true, ['Sniper Shot'] = true, ['Slug Shot'] = true, ['Blast Shot'] = true, ['Heavy Shot'] = true,
    ['Detonator'] = true, ['Numbing Shot'] = true, ['Last Stand'] = true, ['Coronach'] = true, ['Wildfire'] = true,
    ['Trueflight'] = true, ['Leaden Salute'] = true, ['Myrkr'] = true, ['Dagan'] = true, ['Moonlight'] = true,
    ['Starlight'] = true,
};
local reach = 4.7;   -- yalms, for the ones that need you close

-- the weapon skill will not land: too far, under 1,000 TP, or asleep, petrified, stunned, terrorized,
-- amnesiac or charmed; it is cancelled before any gear swaps
local function wsBailout(ws)
    local target = gData.GetActionTarget();
    if (not distant[ws.Name]) and (target ~= nil) and (tonumber(target.Distance) or 0) > reach then
        gFunc.Message('Too far for ' .. ws.Name .. '.');
        return true;
    end
    if (gData.GetPlayer().TP < 1000) then return true; end
    for _, b in ipairs({ 'Sleep', 'Petrification', 'Stun', 'Terror', 'Amnesia', 'Charm' }) do
        if (gData.GetBuffCount(b) > 0) then return true; end
    end
    return false;
end

local moonshadeTP = 2250;   -- below this TP a weapon skill takes Moonshade Earring

-- the spell will not go off: asleep, petrified, stunned, terrorized, silenced, mute or charmed
local function spellBailout()
    for _, b in ipairs({ 'Sleep', 'Petrification', 'Stun', 'Terror', 'Silence', 'Mute', 'Charm' }) do
        if (gData.GetBuffCount(b) > 0) then return true; end
    end
    return false;
end

-- Utsusemi over shadows you still have: the old ones come off just before the new ones land (Ichi
-- 2.4 s into the cast, Ni 0.5 s), or the new cast is wasted. Copy Image is buffs 66, 444, 445, 446.
local function shadows(spell)
    local delay = (spell.Name == 'Utsusemi: Ichi') and 2.4 or ((spell.Name == 'Utsusemi: Ni') and 0.5 or nil);
    if (delay == nil) then return; end
    for _, id in ipairs({ 66, 444, 445, 446 }) do
        if (gData.GetBuffCount(id) > 0) then
            (function() AshitaCore:GetChatManager():QueueCommand(-1, '/cancel ' .. id); end):once(delay);
            return;
        end
    end
end

-- 7. handlers -----------------------------------------------------------------------------------------

profile.OnLoad = function()
    gSettings.AllowAddSet = true;
end

profile.OnUnload = function()
end

-- /vg fwd <word> ... arrives here
profile.HandleCommand = function(args)
    if (args[1] == 'fight') then
        state.fight = not state.fight;
        gFunc.Message('Pet sent in when you engage: ' .. tostring(state.fight));
    end
end

-- Every moment nothing is going off: a pet's move (/BST), standing, fighting, resting.
profile.HandleDefault = function()
    local player = gData.GetPlayer();
    local pet = gData.GetPet();
    local petAction = gData.GetPetAction();

    -- the pet's move, while it goes off: its own set, else PetAtk
    if (petAction ~= nil) then
        if (setFor(petAction.Name) ~= nil) then gFunc.EquipSet(setFor(petAction.Name)); else wear('PetAtk'); end
        -- no set of the move's own: by its kind
        if (setFor(petAction.Name) == nil) then
            if (lists.accuracyReady[petAction.Name]) or (gVana.Toggles.Pet == 'acc') then wear('PetAcc');
            elseif (lists.magicReady[petAction.Name]) then wear('PetMab'); end
        end

        -- yours:
        return;
    end

    wear('Idle');
    if (pet ~= nil) and (pet.Status == 'Engaged') and (player.Status ~= 'Engaged') then
        wear('PetTP');
    end
    if (player.Status == 'Engaged') then
        wear('Tp_' .. (gVana.Toggles.Tp or 'Default'));
    elseif (player.Status == 'Resting') then
        wear('Resting');
    end
    vg.Default();   -- movement, regen and refresh when low, DT, TH, town, the rings in use
    -- only the pet fighting, with /vg pet dt: its damage taken set
    if (pet ~= nil) and (pet.Status == 'Engaged') and (player.Status ~= 'Engaged') and (gVana.Toggles.Pet == 'dt') then
        wear('PetDT');
    end
    -- the pet under half its HP: its damage taken set, whatever the toggle (GetAwayCoxn's include does this)
    if (pet ~= nil) and (pet.HPP < 50) then wear('PetDT'); end
    -- you fighting and the pet not: send it in (GetAwayCoxn's BST.lua), at most every 3 seconds
    if (state.fight) and (pet ~= nil) and (player.Status == 'Engaged') and (pet.Status ~= 'Engaged') and (os.clock() - state.lastFight > 3) then
        state.lastFight = os.clock();
        AshitaCore:GetChatManager():QueueCommand(1, '/ja "Fight" <t>');
    end

    -- yours:
end

-- A job ability as you press it (the subjob's too: Call Beast, Reward, Ready on /BST): its own set.
profile.HandleAbility = function()
    local ability = gData.GetAction();
    local own = setFor(ability.Name);
    if (own ~= nil) then gFunc.EquipSet(own); end
    cancels(ability);
    -- the Beastmaster's pet orders
    if (ability.Type == 'Ready') or (ability.Name == 'Sic') then wear('ReadyPrecast'); end
    if (ability.Name == 'Call Beast') or (ability.Name == 'Bestial Loyalty') then wear('Call'); end

    -- yours:
end

profile.HandleItem = function()
    local item = gData.GetAction();
    local own = setFor(item.Name);
    if (own ~= nil) then gFunc.EquipSet(own); end

    -- yours:
end

-- The subjob's spells: fast cast before the spell starts (and its kind's own, Cure_Precast, Song_Precast)...
profile.HandlePrecast = function()
    local spell = gData.GetAction();
    if (spellBailout()) then
        gFunc.CancelAction();
        return;
    end
    wear('Precast');
    local kind = skillSets[spell.Skill];
    if (kind ~= nil) then wear(kind .. '_Precast'); end
    local family = familyOf(spell);
    if (family ~= nil) then wear(family .. '_Precast'); end
    cancels(spell);
    shadows(spell);
    -- /vg stage: the spell's own gear waits until just before it lands (cast time less the Precast set's fast cast)
    if (gVana.Toggles.Stage) and (spell.CastTime ~= nil) and (sets.Interim ~= nil) and (next(sets.Interim) ~= nil) then
        local delay = ((spell.CastTime * (1 - math.min(0.8, (gVana.FastCast or 0) / 100))) / 1000) - 0.1;
        if (delay >= 0.4) then gFunc.SetMidDelay(delay); end
    end

    -- yours:
end

-- ...then its kind's set, and the spell's own over it, while it casts.
profile.HandleMidcast = function()
    local spell = gData.GetAction();
    -- /vg stage: Interim (spell interruption down, damage taken down) holds while the spell casts, and the
    -- spell's gear below goes on just before it lands
    if (gVana.Toggles.Stage) and (sets.Interim ~= nil) and (next(sets.Interim) ~= nil) then gFunc.InterimEquipSet(sets.Interim); end
    local kind = skillSets[spell.Skill];
    if (kind ~= nil) then wear(kind); end
    -- its family's set (Regen II wears Regen), then the spell's own if it has none or you made one
    local family = familyOf(spell);
    if (family ~= nil) then wear(family); end
    local spellSet = string.gsub(spell.Name, '[^%w]', '');
    if (family == nil) or (yours(spellSet)) then wear(spellSet); end
    if (spell.Skill == 'Enhancing Magic') and (bySkill(spell.Name)) then wear('Enhancing_Skill'); end
    -- vanagear's toggles: /vg burst puts Burst over a nuke; /vg nuke acc puts NukeACC over a nuke and Macc
    -- over an enfeeble
    -- (an elemental ninjutsu, Katon: Ichi to Suiton: San, is a nuke too)
    if (spell.Skill == 'Elemental Magic') or ((spell.Skill == 'Ninjutsu') and (string.find(spell.Name, '^%a+ton:') ~= nil)) then
        if (gVana.Toggles.Nuke == 'acc') then wear('NukeACC'); end
        if (gVana.Toggles.Burst) then wear('Burst'); end
    elseif (spell.Skill == 'Enfeebling Magic') and (gVana.Toggles.Nuke == 'acc') then
        wear('Macc');
    end
    -- the obi over a spell whose element is the day's or the weather's: that element's obi if you have
    -- one (Obi_Fire...), else Obi (Hachirin-no-Obi)
    local env = gData.GetEnvironment();
    if (spell.Element ~= nil) and ((spell.Element == env.DayElement) or (spell.Element == env.WeatherElement)) then
        local own = sets['Obi_' .. spell.Element];
        if (own ~= nil) and (next(own) ~= nil) then gFunc.EquipSet(own); else wear('Obi'); end
    end
    -- cast on yourself: the kind's _Self set over it (Cure_Self, Enhancing_Self)
    local target = gData.GetActionTarget();
    if (target ~= nil) and (target.Name == gData.GetPlayer().Name) then
        if (kind ~= nil) then wear(kind .. '_Self'); end
        if (family ~= nil) then wear(family .. '_Self'); end
    end
    if (gVana.Toggles.TH) then wear('TH'); end   -- /vg th: the spell tags the monster for Treasure Hunter

    -- yours:
end

-- A ranged attack: snapshot gear before the shot...
profile.HandlePreshot = function()
    wear('Preshot');
    -- Flurry gives Snapshot itself (15, Flurry II 30): the set that tops it up and puts Rapid Shot in the rest
    if (gData.GetBuffCount(581) > 0) then wear('Preshot_FlurryII'); elseif (gData.GetBuffCount(265) > 0) then wear('Preshot_Flurry'); end

    -- yours:
end

-- ...then the shot's own gear while it flies.
profile.HandleMidshot = function()
    wear('Midshot');
    if (gVana.Toggles.Tp ~= 'Default') then wear('Midshot_' .. gVana.Toggles.Tp); end   -- /vg tp acc: Midshot_Acc
    if (gVana.Toggles.TH) then wear('TH'); end   -- /vg th: the shot tags the monster for Treasure Hunter

    -- yours:
end

-- A weapon skill: Ws_Default, then its own set over it.
profile.HandleWeaponskill = function()
    local ws = gData.GetAction();
    if (wsBailout(ws)) then
        gFunc.CancelAction();
        return;
    end
    wear('Ws_Default');
    local own = setFor(ws.Name);
    if (own ~= nil) then gFunc.EquipSet(own); end
    if (gVana.Toggles.Tp ~= 'Default') then   -- /vg tp acc: SavageBlade_Acc if you made one, else Ws_Acc
        local inMode = string.gsub(ws.Name, '[^%w]', '') .. '_' .. gVana.Toggles.Tp;
        if (sets[inMode] ~= nil) then wear(inMode); else wear('Ws_' .. gVana.Toggles.Tp); end
    end
    -- under 2,250 TP in the normal mode, Moonshade Earring's TP bonus, if you carry it
    if (gVana.Toggles.Tp == 'Default') and (gData.GetPlayer().TP < moonshadeTP) and (gVana.Carries('Moonshade Earring')) then
        gFunc.Equip('Ear2', 'Moonshade Earring');
    end
    -- a weapon skill whose damage takes an element (Trueflight, Aeolian Edge), on its day or weather:
    -- that element's obi if you have one (Obi_Light...), else Obi (Hachirin-no-Obi)
    local element = vg.WsElement(ws.Name);
    local env = gData.GetEnvironment();
    if (element ~= nil) and ((element == env.DayElement) or (element == env.WeatherElement)) then
        local obi = sets['Obi_' .. element];
        if (obi ~= nil) and (next(obi) ~= nil) then gFunc.EquipSet(obi); else wear('Obi'); end
    end

    -- yours:
end

return profile;
