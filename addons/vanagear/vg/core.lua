--[[
    vanagear: the state the builder keeps, the zone wait, and when sets get rebuilt.

    gVana.Sets holds the built sets by name, ready for gFunc.EquipSet. They are rebuilt when the
    job, subjob, level or weapons change, when gear comes into or leaves the equippable bags, and
    on /vg build. A rebuild is spread over several frames so the game never stalls on it.

    The zone wait: Ferrin, 10 Oct 2026, "luashitacast will crash my server if its set to load in
    right away, it needs a good delay after zones". After every zone-in, nothing here touches the
    game until the client has sent its first position from the new zone and ZoneWait more
    seconds have passed: no profile is loaded, no gear is equipped, no sets are built, no command
    is sent.
]]--

local bags = require('vg.bags');
local plan = require('vg.plan');
local score = require('vg.score');
local vdata = require('vg.data');

local core = {};

gVana = {
    Sets = {},
    Explain = {},
    Totals = {},
    FastCast = 0,
    Held = {},
    Who = nil,
    BuiltAt = nil,
    Toggles = { DT = false, Kite = false, TH = false, Tp = 'Default', Idle = 'auto', Nuke = 'power', Burst = false,
                Stage = false, Pet = 'tp' },
    Prefs = { ZoneWait = 10, Weapons = false, WeaponPick = {}, Locked = {}, Underlay = {}, MoteKeys = false, Look = {} },
    Pieces = {},
    Built = {},
    Own = {},
    SlotBlocks = (vdata.SlotBlocks ~= nil) and vdata.SlotBlocks() or {},
    SetSources = setmetatable({}, { __mode = 'k' }),
};

-- ---------------------------------------------------------------- preferences on disk

local function prefsPath()
    return string.format('%sconfig\\addons\\vanagear\\prefs.lua', AshitaCore:GetInstallPath());
end

local function serialize(v, indent)
    indent = indent or '';
    if (type(v) == 'table') then
        local parts = {};
        for k, x in pairs(v) do
            local key = (type(k) == 'number') and ('[' .. k .. ']') or ('[' .. string.format('%q', k) .. ']');
            parts[#parts + 1] = indent .. '    ' .. key .. ' = ' .. serialize(x, indent .. '    ') .. ',';
        end
        return '{\n' .. table.concat(parts, '\n') .. '\n' .. indent .. '}';
    elseif (type(v) == 'string') then
        return string.format('%q', v);
    end
    return tostring(v);
end

core.SavePrefs = function()
    local folder = string.format('%sconfig\\addons\\vanagear', AshitaCore:GetInstallPath());
    if (not ashita.fs.exists(folder)) then
        ashita.fs.create_directory(folder);
    end
    local f = io.open(prefsPath(), 'w');
    if (f ~= nil) then
        f:write('-- vanagear settings, written by the addon.\nreturn ' .. serialize(gVana.Prefs) .. ';\n');
        f:close();
    end
end

core.LoadPrefs = function()
    local chunk = loadfile(prefsPath());
    if (chunk ~= nil) then
        local ok, saved = pcall(chunk);
        if (ok) and (type(saved) == 'table') then
            for k, v in pairs(saved) do gVana.Prefs[k] = v; end
        end
    end
end

-- ---------------------------------------------------------------- the zone wait

local zone = { Holding = true, EnteredAt = os.clock(), InZoneAt = nil };

-- A slot you change yourself from the equipment menu is no longer held until the next zone: the lua
-- swaps it at its next swap, as LuAshitacast does (Lee, 24 Oct 2026: "take out the hand-change hold too,
-- so only the lua swaps"; it had been asked for 18 Oct 2026). /vg lock keeps a slot as you have it.

-- Called on the incoming zone packet (0x0A).
core.ZoneIn = function()
    zone.Holding = true;
    zone.Leaving = false;
    zone.EnteredAt = os.clock();
    zone.InZoneAt = nil;
end

-- Called on the incoming zone-out packet (0x0B): from here to the next 0x0A the client has no
-- equipment or bags loaded, so the wait starts now and positions sent on the way out don't count.
core.ZoneOut = function()
    core.ZoneIn();
    zone.Leaving = true;
end

-- Called on every outgoing position packet (0x15): the client only sends these once it is
-- standing in the zone.
core.Moved = function()
    if (zone.Holding) and (not zone.Leaving) and (zone.InZoneAt == nil) then
        zone.InZoneAt = os.clock();
    end
end

-- True while the zone wait is running.
core.Holding = function()
    if (not zone.Holding) then
        return false;
    end
    local now = os.clock();
    local start = zone.InZoneAt;
    -- a client that never sends a position (standing still in a cutscene) is let go a minute in
    if (start == nil) and (now - zone.EnteredAt > 60) then
        start = zone.EnteredAt;
    end
    if (start ~= nil) and (now - start >= (gVana.Prefs.ZoneWait or 10)) then
        zone.Holding = false;
        core.OnSettled();
        return false;
    end
    return true;
end

-- ---------------------------------------------------------------- building

local queue = nil;   -- the rebuild in progress: { items = plan, i = next index, ... }
local wanted = false;
local loud = false;

-- loud: say in chat when the build is done (only for /vg build; every other rebuild is silent,
-- Ferrin 18 Oct 2026: "we should make the gear sets message silent").
core.Request = function(sayDone)
    wanted = true;
    if (sayDone) then loud = true; end
end

local function setFor(built)
    local set = {};
    for slot, piece in pairs(built) do
        set[gData.Constants.EquipSlotNames[slot]] = piece.Equip;
    end
    return set;
end

local function startBuild()
    local who = bags.Who();
    if (who.Job == 0) or (who.JobName == '') then
        return;
    end
    local equipment = gData.GetEquipment();
    local items, held = plan.For(who, equipment, { Weapons = gVana.Prefs.Weapons, Locked = gVana.Prefs.Locked[who.JobName] });
    queue = {
        who = who,
        items = items,
        held = held,
        pieces = bags.Read(),
        petTypes = vdata.PetTypes[who.JobName] or { 0 },
        i = 1,
        sets = {},
        explain = {},
        totals = {},
    };
end

local function stepBuild(budget)
    local q = queue;
    local stop = q.i + budget - 1;
    while (q.i <= #q.items) and (q.i <= stop) do
        local item = q.items[q.i];
        -- spells of one kind share one build (every Cure is the Cure set until you change it)
        q.same = q.same or {};
        local done = item.SameAs and q.same[item.SameAs];
        if (done == nil) then
            local mode = score.Compile(item.Mode);
            local built, ranks, total, totals = score.Build(mode, q.pieces, q.who, item.Slots, q.petTypes, true);
            done = { built = built, ranks = ranks, total = total, totals = totals, mode = mode };
            if (item.SameAs) then q.same[item.SameAs] = done; end
        end
        if (next(done.built) ~= nil) then
            q.sets[item.Name] = setFor(done.built);
            q.explain[item.Name] = { ranks = done.ranks, total = done.total, mode = done.mode, weaponskill = item.Weaponskill,
                spell = item.Spell };
            q.totals[item.Name] = done.totals;
        end
        q.i = q.i + 1;
    end
    if (q.i > #q.items) then
        -- swap in the new sets all at once, keeping the same table so profiles holding it see them
        -- written into the job's profile by ApplyOwn; gVana.Built is what the file holds
        gVana.Fresh = { sets = q.sets, job = q.who.JobName };
        gVana.Explain = q.explain;
        gVana.Totals = q.totals;
        gVana.Held = q.held;
        gVana.Who = q.who;
        gVana.Pieces = q.pieces;
        gVana.BuiltAt = os.time();
        core.ApplyOwn();
        core.ApplyLook();
        require('vg.trusts').OnRebuilt();   -- your trusts' luas are built from the same bags, once, now
        local fc = vdata.ModId('FASTCAST');
        gVana.FastCast = (q.totals.Precast and fc) and (q.totals.Precast['m' .. fc] or 0) or 0;
        local count = 0;
        for _ in pairs(q.sets) do count = count + 1; end
        if (loud) then
            loud = false;
            print(chat.header('vanagear') .. chat.message(string.format('Built %d sets for %s%d/%s from your bags. /vg to see them.',
                count, q.who.JobName, q.who.Level, q.who.SubName)));
        end
        queue = nil;
    end
end

-- The sets the handlers wear come out of the job's profile file (vg\profile.lua): a fresh build goes
-- into its built table first, then the file is read back, your sets over built, into gVana.Sets and the
-- profile's own sets table, filled in place so everything holding them sees the change.
core.ApplyOwn = function()
    local vp = require('vg.profile');
    local fresh = gVana.Fresh;
    if (fresh ~= nil) and (not fresh.written) and (vp.WriteBuilt(fresh.sets, fresh.job)) then
        fresh.written = true;
    end
    if (not vp.Refresh()) and (fresh ~= nil) then
        -- no profile file running yet: the sets just built, as they are
        gVana.Built = vp.PlayerBuilt(fresh.sets);
        for k in pairs(gVana.Sets) do gVana.Sets[k] = nil; end
        for k, v in pairs(gVana.Built) do gVana.Sets[k] = v; end
        gVana.Own = {};
        vp.MarkSources(gVana.Sets, {}, gVana.Built);
    end
end

-- The weapon worn in a slot whatever set is on, or nil to leave the slot alone.
core.SetWeaponPick = function(slotName, piece)
    -- The old Range command is a one-time equip, not a job-wide lock.
    if (slotName == 'Range') then
        if (piece ~= nil) then gFunc.ForceEquip('Range', piece); end
        return;
    end
    local job = gVana.Who and gVana.Who.JobName or '';
    if (job == '') then return; end
    gVana.Prefs.WeaponPick = gVana.Prefs.WeaponPick or {};
    gVana.Prefs.WeaponPick[job] = gVana.Prefs.WeaponPick[job] or {};
    gVana.Prefs.WeaponPick[job][slotName] = piece;
    core.SavePrefs();
    core.ApplyOwn();
end

-- ---------------------------------------------------------------- watching for changes

local signature = nil;

-- A string that changes whenever the job, level or weapons do. Gear coming into or leaving the
-- bags (a drop off a mob, the Mog House) waits for the next zone, which rebuilds anyway
-- (Ferrin 18 Oct 2026: "it should work on zones where it finds the best gear for the slot").
local function look()
    local who = bags.Who();
    local parts = { who.Job, who.Level, who.Sub, who.SubLevel };
    local equipment = gData.GetEquipment();
    for _, slot in ipairs({ 'Main', 'Sub', 'Range' }) do
        local e = equipment[slot];
        parts[#parts + 1] = (e and e.Item) and e.Item.Id or 0;
    end
    return table.concat(parts, ',');
end

-- Key binds waiting to be sent, one every half second.
local keyQueue, keyAt, keysBound = {}, 0, false;

core.QueueKeys = function(bind)
    for _, k in ipairs(require('vg.commands').MoteKeys) do
        keyQueue[#keyQueue + 1] = bind and ('/bind ' .. k[1] .. ' ' .. k[2]) or ('/unbind ' .. k[1]);
    end
    keysBound = bind;
end

-- Called every frame.
core.Tick = function()
    if (core.Holding()) then
        return;
    end
    if (gVana.Prefs.MoteKeys) and (not keysBound) then
        core.QueueKeys(true);
    end
    if (#keyQueue > 0) and (os.clock() > keyAt) then
        AshitaCore:GetChatManager():QueueCommand(-1, table.remove(keyQueue, 1));
        keyAt = os.clock() + 0.5;
    end
    if (gState.PendingAutoLoad) then
        gState.PendingAutoLoad = false;
        gState.AutoLoadProfile();
    end
    -- Lee, 24 Oct 2026: no scanning on a timer. What used to be looked at every three seconds (job, level, sub,
    -- weapons) is noted when the packet that changes it arrives: core.NoteChange from packethandlers.lua.
    if (queue == nil) and (wanted) then
        wanted = false;
        startBuild();
    end
    if (queue ~= nil) then
        stepBuild(2);
    end
    require('vg.trusts').Tick();
end

core.OnSettled = function()
    wanted = true;
end

-- Called from the packets that change the job, level or weapons (0x1B, 0x61, 0x50, 0x51): a rebuild is
-- wanted when what the sets were built for has changed, and only then.
core.NoteChange = function()
    if (queue ~= nil) then return; end
    local s = look();
    if (s ~= signature) then
        signature = s;
        wanted = true;
    end
end

-- ---------------------------------------------------------------- the look

-- The set the player's appearance is locked to for this job (the window's "Lock my look" box,
-- /vg look <name>), kept in prefs and put back after every rebuild, so the swaps for actions
-- and overlays never show (Lee, 22 Oct 2026: "style lock the gear set, and stop all the
-- animations"). Any set vanagear has - built, or the player's own - can be the look.
core.LookSet = function()
    local job = gVana.Who and gVana.Who.JobName or '';
    return (gVana.Prefs.Look or {})[job];
end

-- ---------------------------------------------------------------- slot locks

-- One lock (Lee, 24 Oct 2026: the commands cleaned up): /vg lock and the engine's /vg disable are the same
-- thing now. A locked slot is kept as you have it: nothing equips it (gState.Disabled), vanagear builds
-- nothing for it, and the lock is saved for the job and put back each time the job's profile loads.
-- slot: 1-16, or 'all'.
core.SetLock = function(slot, on, jobName)
    jobName = jobName or (gVana.Who and gVana.Who.JobName) or '';
    gVana.Prefs.Locked[jobName] = gVana.Prefs.Locked[jobName] or {};
    local slots = {};
    if (slot == 'all') then for i = 1, 16 do slots[#slots + 1] = i; end else slots[1] = slot; end
    for _, i in ipairs(slots) do
        gVana.Prefs.Locked[jobName][i] = on and true or nil;
        gState.Disabled[i] = on and true or false;
    end
    core.SavePrefs();
    core.Request();
end

-- The job's saved locks back on as its profile loads; slots it does not lock are left as they are.
core.ApplyLocks = function(jobName)
    for i in pairs(gVana.Prefs.Locked[jobName or ''] or {}) do
        gState.Disabled[i] = true;
    end
end

core.SetLook = function(name)
    local job = gVana.Who and gVana.Who.JobName or '';
    if (job == '') then return false; end
    gVana.Prefs.Look = gVana.Prefs.Look or {};
    gVana.Prefs.Look[job] = name;
    core.SavePrefs();
    if (name == nil) then
        AshitaCore:GetChatManager():QueueCommand(1, '/lockstyle off');
        return true;
    end
    return core.ApplyLook();
end

core.ApplyLook = function()
    local name = core.LookSet();
    local set = name and gVana.Sets[name];
    if (set == nil) then return false; end
    local ok = pcall(gFunc.LockStyle, set);
    return ok;
end

-- ---------------------------------------------------------------- helpers for the handlers

-- Does the player carry this piece in a bag they can equip from?
core.Carries = function(name)
    local want = string.lower(name);
    for _, p in ipairs(gVana.Pieces) do
        if (string.lower(p.Name) == want) then return true; end
    end
    return false;
end
gVana.Carries = core.Carries;

-- Does the built table fill the slots your sets leave empty? Yes, unless you turned it off for the job
-- with /vg underlay off: then your sets are worn as written and vanagear's own rules stay out, as a
-- plain LuAshitacast-style profile runs.
core.Underlay = function()
    if (gProfile == nil) then
        return false;
    end
    local job = gProfile.VgJob or (gVana.Who and gVana.Who.JobName) or '';
    local pref = gVana.Prefs.Underlay[job];
    if (pref ~= nil) then
        return pref;
    end
    return true;
end

-- The underlay off: only the profile's own rules and sets run. The zone wait stays: it protects the
-- server (Ferrin, 22 Oct 2026: "keep the wait").
core.PureLac = function()
    return (gProfile ~= nil) and (not core.Underlay());
end

-- ---------------------------------------------------------------- another LuAshitacast loaded as well

-- Players who also load the luashitacast addon themselves get two engines swapping every slot against each
-- other (22 Oct 2026). vanagear counts the equip packets it queues; an injected equip packet it did not queue,
-- on three or more slots within five seconds, means another swapper is running. vanagear then stops swapping
-- for a minute at a time and says why, once. Range and ammo are left out: vanafish swaps rods and bait itself.
local ownSent, ownSentAt = 0, 0;
local foreign = {};
local standDownAt = nil;
local warned = false;

-- Called just before vanagear queues an equip packet (0x50 or 0x51).
core.MarkOwnEquip = function()
    local now = os.clock();
    if (now - ownSentAt > 2) then
        ownSent = 0;
    end
    ownSent = ownSent + 1;
    ownSentAt = now;
end

-- Called for every injected equip packet going out. slots: the zero-based equipment slots it changes.
core.SawInjectedEquip = function(slots)
    local now = os.clock();
    if (ownSent > 0) and (now - ownSentAt <= 2) then
        ownSent = ownSent - 1;
        return;
    end
    for _, s in ipairs(slots) do
        if (s ~= 2) and (s ~= 3) then
            foreign[s] = now;
        end
    end
    local count = 0;
    for s, at in pairs(foreign) do
        if (now - at <= 5) then
            count = count + 1;
        else
            foreign[s] = nil;
        end
    end
    if (count >= 3) then
        standDownAt = now;
        if (not warned) then
            warned = true;
            print(chat.header('vanagear') .. chat.error('LuAshitacast is loaded as well, and the two were swapping your gear against each other. vanagear has stopped swapping.'));
            print(chat.header('vanagear') .. chat.message('vanagear runs your LuAshitacast profiles and /lac commands itself: type /addon unload luashitacast and everything works as before.'));
        end
    end
end

-- True while another swapper has been seen in the last minute.
core.StandingDown = function()
    return (standDownAt ~= nil) and (os.clock() - standDownAt < 60);
end

return core;
