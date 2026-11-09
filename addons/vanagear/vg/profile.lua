--[[
    vanagear: profile files, one per job and one per trust, run the way LuAshitacast ran its own -
    vanagear carries that machinery as its own and never uses the luashitacast folder, except to pull
    a player's LuAshitacast profile over the first time a job loads. Lee, 24 Oct 2026: "make vg operate
    like lac"; "you cant store the gear in memory, hence why lac uses profile"; "vg should have lac
    machinery for itself and never have to touch lac".

    config\addons\vanagear\profiles\<Name>_<id>\<JOB>.lua            a job's profile
    config\addons\vanagear\profiles\<Name>_<id>\trusts\<Trust>.lua   a trust's
    config\addons\vanagear\profiles\<Name>_<id>\backups\             copies made before your sets change

    The profile file is the only store. It holds two tables of gear:
      built  the sets vanagear builds from your bags with the job template (jobs\<JOB>.lua, the band for
             your level); vanagear rewrites this table, and only this one, at each rebuild
      sets   yours: the /vg window's drop-downs, "Save what I'm wearing to this set", /vg addset, and the sets of a
             LuAshitacast profile pulled over
    What is worn is your sets with any empty slot filled from built. A set of yours named Engaged, WS,
    FastCast or DT (mine.Aliases) is the built set it means, so a pulled-over profile's own code wears
    the same gear vanagear's rules do. A drop-down changes only its own slot in the file, so a slot
    holding a list of pieces by level, a BaseSet or a _Priority set stays as written.
]]--

local profile = {};

local function base()
    return string.format('%sconfig\\addons\\vanagear\\profiles\\%s_%u', AshitaCore:GetInstallPath(), gState.PlayerName, gState.PlayerId);
end
profile.Folder = base;

profile.Root = function()
    return string.format('%sconfig\\addons\\vanagear\\profiles', AshitaCore:GetInstallPath());
end

profile.Path = function(job)
    return string.format('%s\\%s.lua', base(), job);
end

profile.TrustPath = function(name)
    return string.format('%s\\trusts\\%s.lua', base(), (string.gsub(name or '', '[^%w_]', '')));
end

-- Is this file one of vanagear's profiles (anything under config\addons\vanagear\profiles)?
profile.IsOurs = function(path)
    local root = string.lower(profile.Root() .. '\\');
    return (type(path) == 'string') and (string.sub(string.lower(path), 1, #root) == root);
end

local function readFile(path)
    local f = io.open(path, 'r');
    if (f == nil) then return nil; end
    local text = f:read('*all');
    f:close();
    return text;
end

local function write(path, text)
    gFileTools.CreateDirectories(path);
    local f = io.open(path, 'w');
    if (f == nil) then
        return false;
    end
    f:write(text);
    f:close();
    return true;
end

-- ---------------------------------------------------------------- what a new profile looks like

local handlers = { 'OnLoad', 'OnUnload', 'HandleCommand', 'HandleDefault', 'HandleAbility', 'HandleItem', 'HandlePrecast',
    'HandleMidcast', 'HandlePreshot', 'HandleMidshot', 'HandleWeaponskill' };

local builtHead = '-- Built by vanagear from your bags with the job template; rewritten at each rebuild. Change gear in sets below.';
local commandsHead = '-- Your commands: /vg <word> runs the lines in order; "wait 2" pauses two seconds.';

-- the job's template that ships with vanagear
profile.JobTemplatePath = function(job)
    return string.format('%saddons\\vanagear\\templates\\%s.lua', AshitaCore:GetInstallPath(), job);
end

profile.JobTemplate = function(name, job)
    local out = {
        string.format('-- vanagear profile for %s, %s.', name, job),
        '-- vanagear\'s own rules (vg\\stock.lua) wear these sets; rules of your own go in the handlers below.',
        'local profile = {};',
        '',
        builtHead,
        'local built = {',
        '};',
        'profile.Built = built;',
        '',
        '-- Your sets: the /vg window, "Save what I\'m wearing to this set" and /vg addset write here. A slot you leave out is',
        '-- filled from built.',
        'local sets = {',
        '};',
        'profile.Sets = sets;',
        '',
        commandsHead,
        'local commands = {',
        '};',
        'profile.Commands = commands;',
        '',
        'profile.Packer = {',
        '};',
        '',
        'profile.IsVanagearStock = true;',
        '',
    };
    for _, h in ipairs(handlers) do
        if (h == 'OnLoad') then
            out[#out + 1] = 'profile.OnLoad = function()';
            out[#out + 1] = '    gSettings.AllowAddSet = true;';
        elseif (h == 'HandleCommand') then
            out[#out + 1] = 'profile.HandleCommand = function(args)';
        else
            out[#out + 1] = 'profile.' .. h .. ' = function()';
        end
        out[#out + 1] = 'end';
        out[#out + 1] = '';
    end
    out[#out + 1] = 'return profile;';
    return table.concat(out, '\n') .. '\n';
end

local function trustTemplate(name)
    return table.concat({
        string.format('-- vanagear profile for the trust %s.', name),
        '-- What the server is sent for it (!trustgear) is sets over built; it gets the stats of the pieces',
        '-- while you carry them.',
        '-- Built by vanagear from your bags with the trust\'s job template (the band for its level); rewritten',
        '-- when it changes.',
        'local built = {',
        '};',
        '',
        '-- Yours, from the trust\'s tab in the /vg window ("remove" = nothing in that slot).',
        'local sets = {',
        '};',
        '',
        'return { Built = built, Sets = sets };',
    }, '\n') .. '\n';
end

-- ---------------------------------------------------------------- writing one table whole

local function setsText(var, sets)
    local names = {};
    for name in pairs(sets) do names[#names + 1] = name; end
    table.sort(names);
    local buffer = { string.format('local %s = {', var) };
    local out = { write = function(self, text) buffer[#buffer + 1] = text; end };
    for _, name in ipairs(names) do
        out:write('\n    ');
        gFileTools.WriteSet(out, name, sets[name]);
        out:write(',');
    end
    out:write('\n}');
    return table.concat(buffer);
end

local function commandsText(words)
    local keys = {};
    for word in pairs(words) do keys[#keys + 1] = word; end
    table.sort(keys);
    local block = { 'local commands = {' };
    for _, word in ipairs(keys) do
        local quoted = {};
        for _, line in ipairs(words[word]) do quoted[#quoted + 1] = string.format('%q', line); end
        block[#block + 1] = string.format('    [%q] = { %s },', word, table.concat(quoted, ', '));
    end
    block[#block + 1] = '}';
    return table.concat(block, '\n');
end

-- Writes `local <var> = { ... }` whole into the file: in place when it is there; otherwise just above the
-- file's return line, with <returned>.<field> = <var>. head: a comment line put above a new one.
local function replaceTable(path, var, block, field, head)
    local text = readFile(path);
    if (text == nil) then
        return false;
    end
    local opener = string.format('local %s = {', var);
    local s, e = string.find(text, opener, 1, true);
    if (s ~= nil) then
        local _, tableEnd = gFileTools.ParseTable(text, e + 1);
        text = string.sub(text, 1, s - 1) .. block .. string.sub(text, tableEnd + 1);
    else
        local rs, _, name = string.find(text, '\nreturn%s+([%w_]+)%s*;?%s*$');
        if (rs == nil) then
            return false;
        end
        text = string.sub(text, 1, rs) .. (head and (head .. '\n') or '') .. block .. ';\n' .. name .. '.' .. field .. ' = ' .. var .. ';\n\n'
            .. string.sub(text, rs + 1);
    end
    return write(path, text);
end

-- ---------------------------------------------------------------- backups

local lastBackup = {};

-- A copy of the profile into backups\ before your sets change, as /vg addset makes one; at most one a
-- minute per file, so a run of drop-down picks is one backup.
local function backup(path)
    if (gSettings ~= nil) and (gSettings.AddSetBackups == false) then
        return;
    end
    if (lastBackup[path] ~= nil) and (os.clock() - lastBackup[path] < 60) then
        return;
    end
    local text = readFile(path);
    if (text == nil) then
        return;
    end
    lastBackup[path] = os.clock();
    local name = string.match(path, '[^\\]+$');
    write(string.format('%s\\backups\\%s_%s', base(), os.date('%Y.%m.%d_%H.%M.%S'), name), text);
end
profile.Backup = backup;

-- ---------------------------------------------------------------- pulling a LuAshitacast profile over

-- The sets and commands vanagear 0.1.20 and before kept beside the profile (config\addons\vanagear\sets
-- and commands), for this job.
local function oldFile(kind, job)
    local chunk = loadfile(string.format('%sconfig\\addons\\vanagear\\%s\\%s.lua', AshitaCore:GetInstallPath(), kind, job));
    if (chunk == nil) then return nil; end
    local ok, t = pcall(chunk);
    return (ok and type(t) == 'table') and t or nil;
end

-- The job's profile, made if it is not there yet: the player's LuAshitacast profile for the job pulled
-- over whole when there is one (its /lac commands now /vg), else the template. Then the sets and commands
-- vanagear kept beside it before go in. Returns the path and whether it was made now.
profile.Ensure = function(job, lacPath)
    local path = profile.Path(job);
    if (ashita.fs.exists(path)) then
        return path, false;
    end
    local text = (lacPath ~= nil) and readFile(lacPath) or nil;
    if (text ~= nil) then
        text = string.gsub(text, '/lac validate', '/vg packvalidate');
        text = string.gsub(text, '/luashitacast ', '/vg ');
        text = string.gsub(text, '/lac ', '/vg ');
    end
    -- no LuAshitacast profile to pull over: the job's template (templates\<JOB>.lua, its sections and
    -- handlers shaped for how the job plays), else the plain one
    local template = nil;
    if (text == nil) then
        template = readFile(profile.JobTemplatePath(job));
    end
    if (not write(path, text or template or profile.JobTemplate(gState.PlayerName, job))) then
        return nil, false;
    end
    if (text ~= nil) then
        replaceTable(path, 'built', 'local built = {\n}', 'Built', builtHead);
        print(chat.header('vanagear') .. chat.message('Pulled your LuAshitacast profile for ' .. job .. ' over to ' .. path .. '.'));
    end
    -- your picks from vanagear 0.1.20 and before, slot by slot over whatever the profile has
    for setName, set in pairs(oldFile('sets', job) or {}) do
        if (type(setName) == 'string') and (type(set) == 'table') then
            for slot, piece in pairs(set) do
                profile.SetSlot(path, setName, slot, piece, true);
            end
        end
    end
    -- your commands from vanagear 0.1.20 and before
    local had = profile.ReadCommands(path) or {};
    local words = {};
    for word, lines in pairs(had) do words[word] = lines; end
    for word, lines in pairs(oldFile('commands', job) or {}) do
        if (type(lines) == 'string') then lines = { lines }; end
        if (type(word) == 'string') and (type(lines) == 'table') and (words[string.lower(word)] == nil) then
            words[string.lower(word)] = lines;
        end
    end
    replaceTable(path, 'commands', commandsText(words), 'Commands', commandsHead);
    return path, true;
end

-- ---------------------------------------------------------------- reading a profile

-- The job's profile written again from its template (/vg template): your sets and your commands carried
-- into the new file, everything else the template's (its sections, handlers and declared sets; built is
-- rewritten at the next rebuild). The old file is copied into backups\ first, every time, whatever the
-- backup setting; if anything after that fails the old file is put back. Returns the backup's path, or
-- nil and why. Lee, 24 Oct 2026.
profile.Retemplate = function(job)
    local path = profile.Path(job);
    local old = readFile(path);
    if (old == nil) then
        return nil, 'there is no ' .. job .. ' profile yet';
    end
    local template = readFile(profile.JobTemplatePath(job));
    if (template == nil) then
        return nil, 'vanagear has no template for ' .. job;
    end
    local p = profile.Run(path);
    if (p == nil) then
        return nil, 'your ' .. job .. ' profile does not load, so its sets cannot be read';
    end
    -- each of your sets as its own text in the old file, so it comes over exactly as written: lists of
    -- pieces by level, augments, comments (written out again, LuAshitacast's set writer would drop a list)
    local mine = {};
    for _, opener in ipairs({ 'local sets = {', 'local Sets = {', 'profile.Sets = {', 'local sets = T{', 'local Sets = T{', 'profile.Sets = T{' }) do
        local _, e = string.find(old, opener, 1, true);
        if (e ~= nil) then
            for _, key in ipairs(gFileTools.ParseTable(old, e + 1)) do
                if (mine[key.Name] == nil) then mine[key.Name] = string.sub(old, key.StartIndex, key.EndIndex); end
            end
            break;
        end
    end
    local words = (type(p.Commands) == 'table') and p.Commands or {};

    local saved = string.format('%s\\backups\\%s_%s.lua', base(), os.date('%Y.%m.%d_%H.%M.%S'), job);
    if (not write(saved, old)) then
        return nil, 'the backup could not be written, so nothing was changed';
    end
    local function undo(why)
        write(path, old);
        return nil, why .. '; your old profile is back';
    end
    if (not write(path, template)) then
        return undo('the template could not be written');
    end
    if (next(mine) ~= nil) and (not gFileTools.SaveSets(path, mine)) then
        return undo('your sets could not be written in');
    end
    if (next(words) ~= nil) and (not profile.WriteCommands(path, words)) then
        return undo('your commands could not be written in');
    end
    if (profile.Run(path) == nil) then
        return undo('the new file would not load');
    end
    return saved;
end

-- The file run as a profile is run, for its tables. nil when it will not load.
local function run(path)
    local chunk = loadfile(path);
    if (chunk == nil) then
        return nil;
    end
    local ok, p = pcall(chunk);
    if (ok) and (type(p) == 'table') then
        return p;
    end
    return nil;
end
profile.Run = run;

profile.ReadSets = function(path)
    local p = run(path);
    return p and p.Sets;
end

profile.ReadCommands = function(path)
    local p = run(path);
    return p and (type(p.Commands) == 'table') and p.Commands or nil;
end

local function copy(t)
    local c = {};
    for k, v in pairs(t) do c[k] = v; end
    return c;
end

local function isSlot(key)
    for _, s in ipairs(gData.Constants.EquipSlotNames) do
        if (s == key) then return true; end
    end
    return false;
end

-- The built set a name of yours means: Engaged -> Tp_Default, any case. A _Priority set stays its own: its
-- slots are lists by level that the profile's gFunc.EvaluateLevels turns into a set of single pieces.
local function keyFor(name, built)
    local key = require('vg.mine').Aliases[string.lower(name)] or name;
    for k in pairs(built) do
        if (string.lower(k) == string.lower(key)) then return k; end
    end
    return key;
end

--[[
    Your sets over built: set name -> set, the names your sets use as well as the built ones; own[key] is
    the slots you gave; names[key] is the name the set has in your file. withBuilt false (the underlay off,
    /vg underlay off): your sets only.
]]--
-- Old profiles can still contain generated weapons/ammo until the next rebuild. Remove only
-- those generated choices, without editing the player's sets or changing trust gear.
profile.PlayerBuilt = function(built)
    local out = {};
    for name, set in pairs(built or {}) do
        if (type(set) == 'table') then
            out[name] = copy(set);
            out[name].Main = nil;
            out[name].Sub = nil;
            out[name].Range = nil;
            out[name].Ammo = nil;
        end
    end
    return out;
end

profile.Merge = function(fileSets, built, withBuilt)
    local worn, own, names = {}, {}, {};
    if (withBuilt ~= false) then
        worn = profile.PlayerBuilt(built);
    end
    local aliased, exact = {}, {};
    for name, set in pairs(fileSets or {}) do
        if (type(name) == 'string') and (type(set) == 'table') then
            local key = keyFor(name, built or {});
            local list = (key ~= name) and aliased or exact;
            list[#list + 1] = { name = name, key = key, set = set };
        end
    end
    table.sort(aliased, function(a, b) return a.name < b.name; end);
    table.sort(exact, function(a, b) return a.name < b.name; end);
    for _, list in ipairs({ aliased, exact }) do
        for _, entry in ipairs(list) do
            local target = worn[entry.key] or {};
            own[entry.key] = own[entry.key] or {};
            for k, v in pairs(entry.set) do
                target[k] = v;
                if (isSlot(k)) then own[entry.key][k] = true; end
            end
            worn[entry.key] = target;
            if (names[entry.key] == nil) or (entry.key == entry.name) then names[entry.key] = entry.name; end
        end
    end
    -- your names for a set (Engaged, WS) wear the same set as the built name
    for _, entry in ipairs(aliased) do
        worn[entry.name] = worn[entry.key];
    end
    return worn, own, names;
end

-- Existing GEO templates dispatch Indi-Fury to IndiFury, not the shared Indi set.
-- Apply the player's Indi slot choices to the real spell sets at refresh time, without
-- rewriting their Lua or choosing a bell. A choice on the individual spell wins.
local indiSpells = {
    'IndiRegen', 'IndiPoison', 'IndiRefresh', 'IndiHaste', 'IndiSTR', 'IndiDEX', 'IndiVIT',
    'IndiAGI', 'IndiINT', 'IndiMND', 'IndiCHR', 'IndiFury', 'IndiBarrier', 'IndiAcumen',
    'IndiFend', 'IndiPrecision', 'IndiVoidance', 'IndiFocus', 'IndiAttunement', 'IndiWilt',
    'IndiFrailty', 'IndiFade', 'IndiMalaise', 'IndiSlip', 'IndiTorpor', 'IndiVex', 'IndiLanguor',
    'IndiSlow', 'IndiParalysis', 'IndiGravity',
};
local function inheritIndi(worn, own, fileSets)
    local family = fileSets.Indi;
    if (type(family) ~= 'table') then return; end
    for _, name in ipairs(indiSpells) do
        for slot, piece in pairs(family) do
            if (isSlot(slot)) and (not (own[name] and own[name][slot])) then
                worn[name] = worn[name] or {};
                worn[name][slot] = (type(piece) == 'table') and copy(piece) or piece;
            end
        end
    end
end

-- The running profile's sets read out of its file again and merged: filled into the same tables the
-- profile's own handlers hold (profile.Sets) and the window reads (gVana.Sets), so both see the change.
profile.Refresh = function()
    if (gProfile == nil) or (not profile.IsOurs(gProfile.FilePath)) then
        return false;
    end
    local p = run(gProfile.FilePath);
    if (p == nil) then
        print(chat.header('vanagear') .. chat.error('Your profile did not load: ' .. tostring(gProfile.FilePath)));
        return false;
    end
    local fileSets = (type(p.Sets) == 'table') and p.Sets or {};
    local built = profile.PlayerBuilt((type(p.Built) == 'table') and p.Built or {});
    local worn, own, names = profile.Merge(fileSets, built, require('vg.core').Underlay());
    if (gProfile.VgTemplate) and (gProfile.VgJob == 'GEO') then
        inheritIndi(worn, own, fileSets);
    end

    -- Main/Sub keep their existing weapons-off policy. Range/Ammo are ordinary per-set choices.
    -- Retain legacy WeaponPick.Range in prefs, but never enforce it across sets.
    local picks = {};
    if (not gVana.Prefs.Weapons) then
        local chosen = (gVana.Prefs.WeaponPick or {})[gProfile.VgJob or ''] or {};
        for name, set in pairs(worn) do
            for _, slot in ipairs({ 'Main', 'Sub' }) do
                set[slot] = chosen[slot];
                if (own[name] ~= nil) then own[name][slot] = nil; end
                if (chosen[slot] ~= nil) then picks[string.lower(chosen[slot])] = true; end
            end
        end
    end
    gVana.WeaponPicks = picks;

    local target = (type(gProfile.Sets) == 'table') and gProfile.Sets or {};
    for k in pairs(target) do target[k] = nil; end
    for k, v in pairs(fileSets) do target[k] = v; end
    for k, v in pairs(worn) do target[k] = v; end
    gProfile.Sets = target;

    for k in pairs(gVana.Sets) do gVana.Sets[k] = nil; end
    for k, v in pairs(worn) do gVana.Sets[k] = v; end
    gVana.Built = built;
    gVana.Own = own;
    gVana.FileSets = fileSets;
    gVana.FileNames = names;
    gVana.Commands = (type(p.Commands) == 'table') and p.Commands or nil;
    return true;
end

-- ---------------------------------------------------------------- writing your sets and the built ones

-- The name your file gives a set (Engaged for the built Tp_Default), or the name itself.
profile.FileName = function(name)
    return (gVana.FileNames and gVana.FileNames[name]) or name;
end

local setsOpeners = { 'local sets = {', 'local Sets = {', 'profile.Sets = {', 'local sets = T{', 'local Sets = T{', 'profile.Sets = T{' };

local function quote(piece)
    return string.format('%q', piece);
end

-- ---------------------------------------------------------------- undo

--[[
    Each change to one of your sets keeps that set's text from just before it, newest last, in
    profiles\<Name>_<id>\undo\<file> (at most 50). Undo puts the newest one for that set back, exactly as
    it was written, and takes it off the list, so a second Undo goes one change further back. Lee, 24
    Oct 2026: "make Undo undo only my last change" (the button had deleted the whole set).
]]--
local undoMax = 50;
local undoCache = {};

local function undoPath(path)
    return string.format('%s\\undo\\%s', base(), string.match(path, '[^\\]+$'));
end

local function readUndo(path)
    if (undoCache[path] == nil) then
        local chunk = loadfile(undoPath(path));
        local ok, list = false, nil;
        if (chunk ~= nil) then ok, list = pcall(chunk); end
        undoCache[path] = (ok and type(list) == 'table') and list or {};
    end
    return undoCache[path];
end

local function writeUndo(path, list)
    undoCache[path] = list;
    local lines = { 'return {' };
    for _, h in ipairs(list) do
        lines[#lines + 1] = string.format('    { set = %q, text = %s },', h.set, h.text and quote(h.text) or 'false');
    end
    lines[#lines + 1] = '};';
    return write(undoPath(path), table.concat(lines, '\n') .. '\n');
end

-- The set's own text in the file (Idle = { ... }), or false when the file has no such set.
local function setText(text, setName)
    for _, opener in ipairs(setsOpeners) do
        local _, e = string.find(text, opener, 1, true);
        if (e ~= nil) then
            for _, k in ipairs(gFileTools.ParseTable(text, e + 1)) do
                if (k.Name == setName) then return string.sub(text, k.StartIndex, k.EndIndex); end
            end
            return false;
        end
    end
    return false;
end

local function remember(path, text, setName)
    local list = readUndo(path);
    list[#list + 1] = { set = setName, text = setText(text, setName) };
    while (#list > undoMax) do table.remove(list, 1); end
    writeUndo(path, list);
end

profile.CanUndo = function(path, setName)
    for _, h in ipairs(readUndo(path)) do
        if (h.set == setName) then return true; end
    end
    return false;
end

-- Your last change to the set taken back. false when there is none to take back.
profile.Undo = function(path, setName)
    local list = readUndo(path);
    for i = #list, 1, -1 do
        if (list[i].set == setName) then
            local h = table.remove(list, i);
            writeUndo(path, list);
            backup(path);
            return gFileTools.SaveSets(path, { [setName] = h.text or false });
        end
    end
    return false;
end

--[[
    One slot of one of your sets in the file. piece nil takes the slot out (built fills it again). Only that
    slot's text changes. noBackup: during the pull-over, which is the first write of a new file.
]]--
profile.SetSlot = function(path, setName, slot, piece, noBackup)
    local text = readFile(path);
    if (text == nil) then
        return false;
    end
    local s, e;
    for _, opener in ipairs(setsOpeners) do
        s, e = string.find(text, opener, 1, true);
        if (s ~= nil) then break; end
    end
    if (s == nil) then
        return false;
    end
    local keys = gFileTools.ParseTable(text, e + 1);
    local entry = nil;
    for _, k in ipairs(keys) do
        if (k.Name == setName) then entry = k; end
    end
    if (entry == nil) and (piece == nil) then
        return true;
    end
    if (not noBackup) then
        backup(path);
        remember(path, text, setName);
    end
    if (entry == nil) then
        return gFileTools.SaveSets(path, { [setName] = { [slot] = piece } });
    end
    local open = string.find(text, '=', entry.StartIndex, true);
    open = open and string.find(text, '{', open, true) or nil;
    if (open == nil) or (open > entry.EndIndex) then
        -- the set is not written out as a table (Idle = sets.Base): written whole from what it reads as
        local p = run(path);
        local set = copy((p and p.Sets and p.Sets[setName]) or {});
        set[slot] = piece;
        return gFileTools.SaveSets(path, { [setName] = set });
    end
    local inner, innerEnd = gFileTools.ParseTable(text, open + 1);
    local found = nil;
    for _, k in ipairs(inner) do
        if (string.lower(k.Name) == string.lower(slot)) then found = k; end
    end
    if (found ~= nil) then
        if (piece == nil) then
            local after = found.EndIndex + 1;
            if (string.sub(text, after, after) == ',') then after = after + 1; end
            text = string.sub(text, 1, found.StartIndex - 1) .. string.gsub(string.sub(text, after), '^[ \t]*\r?\n', '', 1);
        else
            text = string.sub(text, 1, found.StartIndex - 1) .. slot .. ' = ' .. quote(piece) .. string.sub(text, found.EndIndex + 1);
        end
    elseif (piece ~= nil) then
        local last = inner[#inner];
        local head;
        if (last ~= nil) and (string.sub(text, last.EndIndex + 1, last.EndIndex + 1) ~= ',') then
            head = string.sub(text, 1, last.EndIndex) .. ',' .. string.sub(text, last.EndIndex + 1, innerEnd - 1);
        else
            head = string.sub(text, 1, innerEnd - 1);
        end
        text = head .. '    ' .. slot .. ' = ' .. quote(piece) .. ',\n    ' .. string.sub(text, innerEnd);
    end
    return write(path, text);
end

-- A whole set of yours into the file (Save what I'm wearing to this set), or out of it with set = false.
profile.SaveSet = function(path, setName, set)
    backup(path);
    local text = readFile(path);
    if (text ~= nil) then remember(path, text, setName); end
    return gFileTools.SaveSets(path, { [setName] = set });
end

-- The built sets into the file's built table, whole. Only for the profile's own job: right after a job
-- change the sets in hand are still the last job's.
profile.WriteBuilt = function(built, job)
    if (gProfile == nil) or (not profile.IsOurs(gProfile.FilePath)) or (job ~= gProfile.VgJob) then
        return false;
    end
    if (not replaceTable(gProfile.FilePath, 'built', setsText('built', built), 'Built', builtHead)) then
        print(chat.header('vanagear') .. chat.error('Could not write the built sets into ' .. gProfile.FilePath .. '.'));
        return false;
    end
    return true;
end

-- ---------------------------------------------------------------- your commands

profile.WriteCommands = function(path, words)
    return replaceTable(path, 'commands', commandsText(words), 'Commands', commandsHead);
end

-- ---------------------------------------------------------------- trusts

local function ensureTrust(name)
    local path = profile.TrustPath(name);
    if (ashita.fs.exists(path)) or (write(path, trustTemplate(name))) then
        return path;
    end
    return nil;
end

-- A trust's sets of yours as its file holds them, or nil when it has no file yet.
profile.ReadTrustSets = function(name)
    local p = run(profile.TrustPath(name));
    return p and (type(p.Sets) == 'table') and p.Sets or nil;
end

-- A trust's sets of yours into its file, the whole table: a set no longer there comes out.
profile.WriteTrustSets = function(name, sets)
    local path = ensureTrust(name);
    if (path == nil) then return false; end
    backup(path);
    return replaceTable(path, 'sets', setsText('sets', sets), 'Sets');
end

-- A trust's built sets into its file; then the file read back and merged, yours over built: what is sent.
profile.WriteTrust = function(name, built)
    local path = ensureTrust(name);
    if (path == nil) then return nil; end
    if (built ~= nil) and (not replaceTable(path, 'built', setsText('built', built), 'Built')) then
        return nil;
    end
    local p = run(path);
    if (p == nil) then return nil; end
    local out = {};
    for setName, set in pairs((type(p.Built) == 'table') and p.Built or {}) do out[setName] = copy(set); end
    for setName, set in pairs((type(p.Sets) == 'table') and p.Sets or {}) do
        out[setName] = out[setName] or {};
        for slot, piece in pairs(set) do
            if (piece == 'remove') then out[setName][slot] = nil; else out[setName][slot] = piece; end
        end
    end
    return out;
end

return profile;
