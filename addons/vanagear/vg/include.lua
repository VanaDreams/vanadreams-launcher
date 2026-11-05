--[[
    vanagear: the checks every job template calls (local vg = gVanaInclude in templates\<JOB>.lua), the same
    for every job, so a template's own handlers stay about the job.

    vg.Default() runs at the end of a template's HandleDefault, over the standing, fighting or resting set
    the template put on: the idle overlays (Idle_Regen, Idle_Refresh, Movement, by /vg idle), Town in a town,
    the toggles (/vg dt, /vg kite, /vg th), the sets that go on by themselves (Dt under 40% HP, PetDT with the
    pet under half its HP, Doomed under Doom or Bane, Sleeping asleep, Reraise weakened). These follow
    GetAwayCoxn's gcinclude.lua (CheckDefault, SetRegenRefreshGear, CheckCommonDebuffs, SetTownGear). A ring
    being used holds its slot over all of this by itself (vg\use.lua).

    vg.WsElement(name) is the element a weapon skill's damage takes, for the obi (data\wselements.lua).
]]--

local vdata = require('vg.data');

local include = {};

-- the towns where Town goes on (GetAwayCoxn's gcinclude.lua list)
local towns = {
    ['Tavnazian Safehold'] = true, ['Al Zahbi'] = true, ['Aht Urhgan Whitegate'] = true, ['Nashmau'] = true,
    ['Southern San d\'Oria [S]'] = true, ['Bastok Markets [S]'] = true, ['Windurst Waters [S]'] = true,
    ['San d\'Oria-Jeuno Airship'] = true, ['Bastok-Jeuno Airship'] = true, ['Windurst-Jeuno Airship'] = true,
    ['Kazham-Jeuno Airship'] = true, ['Southern San d\'Oria'] = true, ['Northern San d\'Oria'] = true,
    ['Port San d\'Oria'] = true, ['Chateau d\'Oraguille'] = true, ['Bastok Mines'] = true, ['Bastok Markets'] = true,
    ['Port Bastok'] = true, ['Metalworks'] = true, ['Windurst Waters'] = true, ['Windurst Walls'] = true,
    ['Port Windurst'] = true, ['Windurst Woods'] = true, ['Heavens Tower'] = true, ['Ru\'Lude Gardens'] = true,
    ['Upper Jeuno'] = true, ['Lower Jeuno'] = true, ['Port Jeuno'] = true, ['Rabao'] = true, ['Selbina'] = true,
    ['Mhaura'] = true, ['Kazham'] = true, ['Norg'] = true, ['Mog Garden'] = true, ['Celennia Memorial Library'] = true,
    ['Western Adoulin'] = true, ['Eastern Adoulin'] = true,
};

-- the profile's set by name (sets over built), put on only if it has gear in it
local function wear(name)
    local set = (gProfile ~= nil) and (type(gProfile.Sets) == 'table') and gProfile.Sets[name] or nil;
    if (set ~= nil) and (next(set) ~= nil) then
        gFunc.EquipSet(set);
        return true;
    end
    return false;
end

-- the sets that go on at low HP or MP come off only at a further line (vg\latch.lua), for templates too
local latch = require('vg.latch');
include.Latch = latch.Latch;
include.Low = latch.Low;
local low = latch.Low;

include.Default = function()
    local t = gVana.Toggles;
    local player = gData.GetPlayer();
    local pet = gData.GetPet();

    -- standing: the idle overlays, the ones that went on by themselves keeping your own Idle picks
    if (player.Status == 'Idle') then
        local idle = t.Idle or 'auto';
        local auto = {};
        if (idle == 'regen') then wear('Idle_Regen');
        elseif (idle == 'auto') and (low('Idle_Regen', player.HPP)) and (wear('Idle_Regen')) then auto[#auto + 1] = 'Idle_Regen'; end
        if (idle == 'refresh') then wear('Idle_Refresh');
        elseif (idle == 'auto') and (player.MaxMP > 0) and (low('Idle_Refresh', player.MPP)) and (wear('Idle_Refresh')) then auto[#auto + 1] = 'Idle_Refresh'; end
        if (idle == 'speed') then wear('Movement');
        elseif (player.IsMoving) and (not t.Kite) and (wear('Movement')) then auto[#auto + 1] = 'Movement'; end
        if (#auto > 0) then gVanaStock.HoldOwn('Idle', auto); end
        local env = gData.GetEnvironment();
        if (env ~= nil) and (towns[env.Area or '']) then wear('Town'); end
    end

    -- the toggles, and what goes on by itself
    if (t.Kite) then wear('Movement'); end
    if (t.TH) and (player.Status == 'Engaged') then wear('TH'); end
    -- /vg dt: all (Dt), pdt (Dt_Physical) or mdt (Dt_Magic); under 40% HP, Dt by itself
    if (t.DT == 'pdt') then wear('Dt_Physical'); elseif (t.DT == 'mdt') then wear('Dt_Magic'); end
    local lowHp = low('Dt', player.HPP);
    if ((t.DT) and (t.DT ~= 'pdt') and (t.DT ~= 'mdt')) or (lowHp) then wear('Dt'); end
    if (pet ~= nil) and (low('PetDT', pet.HPP or 100)) then wear('PetDT'); end

    -- ailments, last: what you wear for them beats everything above
    if (gData.GetBuffCount('Sleep') > 0) then wear('Sleeping'); end
    if (gData.GetBuffCount('Doom') > 0) or (gData.GetBuffCount('Bane') > 0) then wear('Doomed'); end
    if (gData.GetBuffCount('Weakened') > 0) then wear('Reraise'); end
end

include.WsElement = function(name)
    return vdata.WsElement(name);
end

return include;
