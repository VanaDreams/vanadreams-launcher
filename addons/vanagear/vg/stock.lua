--[[
    vanagear: the rules that put the built sets on.

    These run for every job. With no profile of the player's own they are the whole profile;
    with one, they run first and the profile's own rules run after them, so anything the
    profile equips wins slot by slot and anything it leaves alone keeps the built piece.

    The order sets are laid on follows GetAwayCoxn's all-jobs retail profiles: a broad set
    first (Precast, Cure, Nuke), then the narrower ones over it (Cure_Precast, Burst, Drain).
]]--

local lists = require('vg.lists');

local stock = {};

-- The sets come out of the running profile file (vg\profile.lua), where vanagear writes them;
-- from memory only when no vanagear profile could be made.
local function sets()
    if (gProfile ~= nil) and (type(gProfile.Sets) == 'table') then
        return gProfile.Sets;
    end
    return gVana.Sets;
end

local function wear(name)
    local set = sets()[name];
    if (set ~= nil) then
        gFunc.EquipSet(set);
        return true;
    end
    return false;
end

-- spell family sets laid over the skill set
local function family(spell)
    local name = spell.Name or '';
    if (spell.Skill == 'Healing Magic') then
        if (string.find(name, '^Cur')) then wear('Cure'); end
        if (name == 'Cursna') then wear('Cursna'); end
    elseif (spell.Skill == 'Enhancing Magic') then
        if (string.find(name, '^Stoneskin')) then wear('Stoneskin');
        elseif (string.find(name, '^Phalanx')) then wear('Phalanx');
        elseif (string.find(name, '^Regen')) then wear('Regen');
        elseif (string.find(name, '^Refresh')) then wear('Refresh'); end
    elseif (spell.Skill == 'Dark Magic') then
        if (string.find(name, '^Drain')) or (string.find(name, '^Aspir')) then wear('Drain'); end
    end
end

local skillSet = {
    ['Healing Magic'] = 'Cure',
    ['Enhancing Magic'] = 'Enhancing',
    ['Enfeebling Magic'] = 'Enfeebling',
    ['Elemental Magic'] = 'Nuke',
    ['Dark Magic'] = 'Dark',
    ['Divine Magic'] = 'Divine',
    ['Ninjutsu'] = 'Ninjutsu',
    ['Singing'] = 'Song',
    ['Geomancy'] = 'Geomancy',
};

local function overlays()
    local t = gVana.Toggles;
    if (t.DT == 'pdt') then wear('Dt_Physical'); elseif (t.DT == 'mdt') then wear('Dt_Magic'); elseif (t.DT) then wear('Dt'); end
    if (t.Kite) then wear('Movement'); end
    if (t.TH) then wear('TH'); end
end

-- The slots the player picked by hand in a set (the window's drop-downs, /vg set) go back on
-- over the sets laid on it by themselves - Movement while moving, the Regen and Refresh idles
-- on low HP or MP - unless the player also picked that slot in the overlay set. Lee, 22 Oct
-- 2026: "i can see it putting on movement gear underneath instead of the drop down i chose".
-- An overlay the player asked for by a toggle (/vg idle speed, /vg kite) is not touched here.
local function holdOwn(name, overlaysWorn)
    local own = gVana.Own and gVana.Own[name];
    local set = sets()[name];
    if (own == nil) or (set == nil) then return; end
    local keep = {};
    for slot in pairs(own) do
        local pickedInOverlay = false;
        for _, o in ipairs(overlaysWorn) do
            if (gVana.Own[o] ~= nil) and (gVana.Own[o][slot]) then pickedInOverlay = true; end
        end
        if (not pickedInOverlay) and (set[slot] ~= nil) then keep[slot] = set[slot]; end
    end
    if (next(keep) ~= nil) then gFunc.EquipSet(keep); end
end

stock.HoldOwn = holdOwn;   -- the job templates' shared checks use it too (vg\include.lua)

-- Resting laid over Idle by itself: the player's picks in Idle hold. The swaps for an action (an
-- ability, a spell, a weaponskill) are meant to go on over everything and are not touched.
local function wearOver(narrow, broad)
    if (wear(narrow)) then
        holdOwn(broad, { narrow });
        return true;
    end
    return false;
end

stock.HandleDefault = function()
    local t = gVana.Toggles;
    local player = gData.GetPlayer();
    local pet = gData.GetPet();
    local petAction = gData.GetPetAction();

    if (petAction ~= nil) and (sets().BPWard ~= nil) then
        -- a summoner's avatar: the blood pact decides the set
        local bp = petAction.Name or '';
        if (lists.BPBuff[bp]) then wear('BPWard');
        elseif (lists.BPDebuff[bp]) then wear('PetAcc');
        elseif (lists.BPMagical[bp]) then wear('PetMab');
        else wear('PetAtk'); end
        return;
    end
    if (petAction ~= nil) and (sets().PetAtk ~= nil) then
        if (lists.EnfeebleReady[petAction.Name or ''] or t.Pet == 'acc') then
            wear('PetAcc');
        elseif (lists.MagicReady[petAction.Name or '']) then
            wear('PetMab');
        else
            wear('PetAtk');
        end
        return;
    end

    if (player.Status == 'Engaged') then
        wear('Tp_' .. (t.Tp or 'Default'));
    elseif (player.Status == 'Resting') then
        wear('Idle');
        wearOver('Resting', 'Idle');
    elseif (pet ~= nil) and (pet.Status == 'Engaged') and (sets().PetTP ~= nil) then
        wear((t.Pet == 'dt') and 'PetDT' or 'PetTP');
    else
        wear('Idle');
        local idle = t.Idle or 'auto';
        local auto = {};   -- the overlays that went on by themselves, not by a toggle
        if (idle == 'regen') then wear('Idle_Regen');
        elseif (idle == 'auto') and (player.HPP < 60) and (wear('Idle_Regen')) then auto[#auto + 1] = 'Idle_Regen'; end
        if (idle == 'refresh') then wear('Idle_Refresh');
        elseif (idle == 'auto') and (player.MaxMP > 0) and (player.MPP < 70) and (wear('Idle_Refresh')) then auto[#auto + 1] = 'Idle_Refresh'; end
        if (idle == 'speed') then wear('Movement');
        elseif (player.IsMoving) and (not t.Kite) and (wear('Movement')) then auto[#auto + 1] = 'Movement'; end
        if (#auto > 0) then holdOwn('Idle', auto); end
    end
    overlays();
end

local quickDraw = { ['Fire Shot'] = 1, ['Ice Shot'] = 1, ['Wind Shot'] = 1, ['Earth Shot'] = 1, ['Thunder Shot'] = 1,
    ['Water Shot'] = 1, ['Light Shot'] = 2, ['Dark Shot'] = 2 };

stock.HandleAbility = function()
    local action = gData.GetAction();
    local name = action.Name or '';
    -- a set built for this very ability (jobs\<JOB>.lua Abilities) comes first
    if (wear((string.gsub(name, '[^%w]', '')))) then
        return;
    end
    if (string.find(action.Type or '', '^Blood Pact')) then
        wear('BPDelay');
    elseif (string.find(name, ' Roll$')) or (name == 'Double-Up') then
        wear('PhantomRoll');
    elseif (quickDraw[name] ~= nil) then
        -- Light Shot sleeps and Dark Shot dispels: they only need to land
        wear((quickDraw[name] == 2 or gVana.Toggles.Nuke == 'acc') and 'QuickDrawAcc' or 'QuickDraw');
    elseif (action.Type == 'Ready') or (name == 'Sic') then
        wear('ReadyPrecast');
    elseif (name == 'Reward') then
        wear('Reward');
    elseif (name == 'Charm') then
        wear('Charm');
    elseif (name == 'Call Beast') or (name == 'Bestial Loyalty') then
        wear('Call');
    end
end

stock.HandleItem = function()
end

stock.HandlePrecast = function()
    local spell = gData.GetAction();
    wear('Precast');
    if (spell.Skill == 'Healing Magic') and (string.find(spell.Name or '', '^Cur')) then
        wear('Cure_Precast');
    elseif (spell.Skill == 'Singing') then
        wear('Song_Precast');
    end
    if (gVana.Toggles.Stage) and (spell.CastTime ~= nil) and (sets().Interim ~= nil) then
        -- the built Precast set's fast cast; a low guess would land the midcast gear late
        local fastCast = math.min(0.8, (gVana.FastCast or 0) / 100);
        local delay = ((spell.CastTime * (1 - fastCast)) / 1000) - 0.1;
        if (delay >= 0.4) then
            gFunc.SetMidDelay(delay);
        end
    end
end

stock.HandleMidcast = function()
    local t = gVana.Toggles;
    local spell = gData.GetAction();
    if (t.Stage) and (sets().Interim ~= nil) then
        gFunc.InterimEquipSet(sets().Interim);
    end
    if (spell.Skill == 'Blue Magic') then
        wear(lists.BluePhysical[spell.Name or ''] and 'BluePhysical' or 'BlueMagical');
    elseif (skillSet[spell.Skill] ~= nil) then
        wear(skillSet[spell.Skill]);
    end
    -- the set built for this very spell, under the toggles (burst, accuracy, obi) laid on next;
    -- a set of your own for it goes on last, over everything
    local spellSet = (string.gsub(spell.Name or '', '[^%w]', ''));
    local yours = (gVana.Own ~= nil) and (gVana.Own[spellSet] ~= nil) and (next(gVana.Own[spellSet]) ~= nil);
    if (not yours) then wear(spellSet); end
    if (spell.Skill == 'Elemental Magic') then
        if (t.Nuke == 'acc') then wear('NukeACC'); end
        if (t.Burst) then wear('Burst'); end
        local env = gData.GetEnvironment();
        if (spell.Element == env.WeatherElement) or (spell.Element == env.DayElement) then
            if (gVana.Carries('Hachirin-no-Obi')) then gFunc.Equip('Waist', 'Hachirin-no-Obi'); end
        end
    elseif (spell.Skill == 'Enfeebling Magic') and (t.Nuke == 'acc') then
        wear('Macc');
    end
    family(spell);
    -- a set of your own named for this very spell (as Cure IV or CureIV)
    if (yours) then wear(spellSet); end
    if (t.TH) then wear('TH'); end
end

stock.HandlePreshot = function()
    wear('Preshot');
end

stock.HandleMidshot = function()
    wear('Midshot');
    if (gVana.Toggles.TH) then wear('TH'); end
end

stock.HandleWeaponskill = function()
    local ws = gData.GetAction();
    if (not wear((string.gsub(ws.Name or '', '[^%w]', '')))) then
        wear('Ws_Default');
    end
end

return stock;
