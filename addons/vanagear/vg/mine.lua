--[[
    vanagear: the player's own commands. /vg <word> runs the lines saved under that word, in order.

    They live in the job's profile, config\addons\vanagear\profiles\<Name>_<id>\<JOB>.lua, in its
    commands table (ALL.lua beside the profiles for every job), so a player can write them by hand.
    The /vg window and /vg add write the same place. A line is any game command (/ja "Reward" <me>,
    /vg call fizzy); "wait 2" pauses two seconds before the next line.

    Your own sets live in the same profile, in its sets table (vg\profile.lua), made in the window slot
    by slot, with /vg set <name> <slot> <item>, from what you wear with /vg save <name>, or with /vg
    addset. One named like a built set (Idle, Tp_Default, Reward, a weaponskill or spell) fills its
    slots over the built set wherever it goes on; any other name is a set of its own for /vg wear
    <name>, your commands, or /gs equip. /vg look <name> locks one as your appearance (/lockstyle).
]]--

local mine = {};

local running = nil;   -- { lines, index, at }

local function say(text)
    print(chat.header('vanagear') .. chat.message(text));
end

mine.Folder = function(kind)
    return string.format('%sconfig\\addons\\vanagear\\%s', AshitaCore:GetInstallPath(), kind or 'commands');
end

local function path(file, kind)
    return string.format('%s\\%s.lua', mine.Folder(kind), file);
end

local function job()
    return gVana.Who and gVana.Who.JobName or '';
end

local cache = {};   -- file -> { word -> { line, ... } }

-- A job's commands live in its profile file, in its commands table (vg\profile.lua; Lee, 24 Oct 2026:
-- "they are supposed to be written into the fucking lua as well"). The words for every job live in
-- profiles\<Name>_<id>\ALL.lua beside the profiles; what commands\ALL.lua held before is read the first time.
local function allPath()
    return require('vg.profile').Folder() .. '\\ALL.lua';
end

local function words(saved)
    local out = {};
    for word, lines in pairs(saved or {}) do
        if (type(lines) == 'string') then lines = { lines }; end
        if (type(word) == 'string') and (type(lines) == 'table') then
            out[string.lower(word)] = lines;
        end
    end
    return out;
end

local function load(file)
    if (cache[file] ~= nil) then
        return cache[file];
    end
    local vp = require('vg.profile');
    if (file == 'ALL') then
        local chunk = loadfile(allPath()) or loadfile(path('ALL'));
        local ok, saved = false, nil;
        if (chunk ~= nil) then ok, saved = pcall(chunk); end
        cache[file] = words((ok and type(saved) == 'table') and saved or nil);
    else
        cache[file] = words(vp.ReadCommands(vp.Path(file)));
    end
    return cache[file];
end

local function save(file)
    local vp = require('vg.profile');
    local list = cache[file] or {};
    if (file ~= 'ALL') then
        if (not ashita.fs.exists(vp.Path(file))) or (not vp.WriteCommands(vp.Path(file), list)) then
            say('Could not write your commands into ' .. vp.Path(file));
        end
        return;
    end
    local keys = {};
    for word in pairs(list) do keys[#keys + 1] = word; end
    table.sort(keys);
    local out = {
        '-- vanagear commands for every job. /vg <word> runs the lines in order.',
        '-- A line is any game command; "wait 2" pauses two seconds. Edit here or in the /vg window.',
        'return {',
    };
    for _, word in ipairs(keys) do
        local quoted = {};
        for _, line in ipairs(list[word]) do quoted[#quoted + 1] = string.format('%q', line); end
        out[#out + 1] = string.format('    [%q] = { %s },', word, table.concat(quoted, ', '));
    end
    out[#out + 1] = '};';
    gFileTools.CreateDirectories(allPath());
    local f = io.open(allPath(), 'w');
    if (f == nil) then
        say('Could not write ' .. allPath());
        return;
    end
    f:write(table.concat(out, '\n') .. '\n');
    f:close();
end

-- Forget what was read, so hand edits are picked up.
mine.Reload = function()
    cache = {};
    require('vg.core').ApplyOwn();
end

-- word -> { lines, file } for this job, the job's own file winning over ALL.lua
mine.List = function()
    local list = {};
    for word, lines in pairs(load('ALL')) do list[word] = { lines = lines, file = 'ALL' }; end
    if (job() ~= '') then
        for word, lines in pairs(load(job())) do list[word] = { lines = lines, file = job() }; end
    end
    return list;
end

-- Lines typed as one string, split on ';' or new lines.
mine.Split = function(text)
    local lines = {};
    for part in string.gmatch(text or '', '[^;\n]+') do
        local line = string.match(part, '^%s*(.-)%s*$');
        if (line ~= '') then lines[#lines + 1] = line; end
    end
    return lines;
end

mine.Add = function(word, lines, everyJob)
    word = string.lower(string.gsub(word or '', '[^%w_%-]', ''));
    if (word == '') or (#lines == 0) then
        return false;
    end
    local file = (everyJob or job() == '') and 'ALL' or job();
    load(file)[word] = lines;
    save(file);
    return true, word, file;
end

mine.Remove = function(word)
    word = string.lower(word or '');
    local entry = mine.List()[word];
    if (entry == nil) then
        return false;
    end
    load(entry.file)[word] = nil;
    save(entry.file);
    return true, entry.file;
end

-- Open config\addons\vanagear in Windows Explorer: commands\ and sets\ are in it, made first
-- with this job's file so there is something to edit.
mine.OpenFolder = function()
    -- your profiles, which hold your sets and commands (vg\profile.lua)
    local folder = require('vg.profile').Folder();
    gFileTools.CreateDirectories(folder .. '\\');
    os.execute(string.format('start "" explorer "%s"', folder));
    return folder;
end

mine.Run = function(word)
    local entry = mine.List()[string.lower(word or '')];
    if (entry == nil) then
        return false;
    end
    if (running ~= nil) then
        say('Your command ' .. running.word .. ' is still going.');
        return true;
    end
    local lines = {};
    for i, l in ipairs(entry.lines) do lines[i] = l; end
    running = { word = string.lower(word), lines = lines, index = 1, at = 0 };
    return true;
end

mine.Stop = function()
    if (running ~= nil) then
        say('Your command ' .. running.word .. ' stopped.');
        running = nil;
    end
end

-- Chains (M402): a line "then <command>" goes once the action the line before it sent has
-- finished, plus the gap the server wants between links; "burst <command>" goes the moment it
-- finishes. The watching is what Lee's BST profile does by hand (chainTick): see the step start
-- in gData.GetAction (or GetPetAction for a /pet line), see it end, then send. A step that never
-- starts, or a target that dies or changes, stops the whole command.
mine.LinkGap = 3.3;        -- seconds after a link finishes before the next may land (battleutils, 3 s rule)
mine.StepTimeout = 8;      -- seconds to see a step start before giving up

-- the action a line sends, if it is one 'then' can wait on: /ws, /ja, /ma or /pet "Name" ...
local function actionOf(line)
    local cmd, name = string.match(line, '^/(%a+)%s+"([^"]+)"');
    if (cmd == 'ws') or (cmd == 'ja') or (cmd == 'ma') or (cmd == 'pet') then
        return { kind = cmd, name = string.lower(name) };
    end
    return nil;
end

local function currentAction(kind)
    local ok, a = pcall(function()
        return (kind == 'pet') and gData.GetPetAction() or gData.GetAction();
    end);
    return ok and a or nil;
end

local function targetGone(w)
    local ok, t = pcall(gData.GetTarget);
    if (not ok) then return false; end
    return (t == nil) or (t.Id ~= w.targetId) or ((t.HPP or 1) <= 0);
end

-- One look at the action a 'then' or 'burst' line is waiting on. Looked at the moment the watch
-- is set as well as every frame after, so a quick ability is not missed between two frames.
local function watch(r, now)
    local w = r.watch;
    if (targetGone(w)) then
        say('Your command ' .. r.word .. ': target lost, stopped.');
        running = nil;
        return;
    end
    local a = currentAction(w.kind);
    if (w.phase == 'starting') then
        if (a ~= nil) and (string.lower(a.Name or '') == w.name) then
            w.phase = 'running';
        elseif (now - w.sentAt > mine.StepTimeout) then
            say('Your command ' .. r.word .. ': ' .. w.name .. ' never started, stopped.');
            running = nil;
        end
        return;
    end
    if (a ~= nil) then
        return;
    end
    r.watch = nil;
    r.at = now + w.gap;
end

-- Called every frame: send the next line once any wait is over.
mine.Tick = function()
    local r = running;
    if (r == nil) or (os.clock() < r.at) then
        return;
    end
    local now = os.clock();
    -- a 'then' or 'burst' line is waiting for the action before it
    if (r.watch ~= nil) then
        watch(r, now);
        return;
    end
    local line = r.lines[r.index];
    if (line == nil) then
        running = nil;
        return;
    end
    local rest, gap = string.match(line, '^[Tt][Hh][Ee][Nn]%s+(.+)$'), mine.LinkGap;
    if (rest == nil) then
        rest, gap = string.match(line, '^[Bb][Uu][Rr][Ss][Tt]%s+(.+)$'), 0;
    end
    if (rest ~= nil) then
        if (r.last ~= nil) then
            -- wait for the last action, then come back to this line without its prefix
            r.watch = { kind = r.last.kind, name = r.last.name, sentAt = r.last.sentAt, targetId = r.last.targetId, phase = 'starting', gap = gap };
            r.last = nil;
            r.lines[r.index] = rest;
            watch(r, now);
            return;
        end
        line = rest;   -- nothing before it to wait for: it just goes
    end
    r.index = r.index + 1;
    local secs = tonumber(string.match(line, '^[Ww][Aa][Ii][Tt]%s+([%d%.]+)$'));
    if (secs ~= nil) then
        r.at = os.clock() + secs;
        return;
    end
    -- remember an action line so a 'then' after it has something to wait for
    local a = actionOf(line);
    if (a ~= nil) then
        local ok, t = pcall(gData.GetTarget);
        a.sentAt = now;
        a.targetId = (ok and t ~= nil) and t.Id or nil;
    end
    r.last = a;
    local first = string.lower(string.match(line, '^(%S+)') or '');
    if (first == '/vg') or (first == '/vanagear') or (first == '/gs') or (first == '/gearswap') then
        -- vanagear's own commands run straight away, never back through the chat queue
        local args = {};
        for w in string.gmatch(line, '%S+') do args[#args + 1] = (string.gsub(w, '^"(.*)"$', '%1')); end
        table.remove(args, 1);
        local commands = require('vg.commands');
        commands.Raw = line;
        if (first == '/gs') or (first == '/gearswap') then commands.Gs(args); else commands.Vg(args); end
        commands.Raw = nil;
        r.at = os.clock() + 0.3;
        return;
    end
    AshitaCore:GetChatManager():QueueCommand(1, line);
    r.at = os.clock() + 0.3;
end

-- ---------------------------------------------------------------- your own sets

-- Your sets are the sets table of the job's profile file (vg\profile.lua): read out of it when the
-- profile is read (gVana.FileSets), and every change written straight into it.
local function profilePath()
    return (gProfile ~= nil) and gProfile.FilePath or nil;
end

mine.Sets = function()
    local out = {};
    for k, set in pairs(gVana.FileSets or {}) do
        if (type(set) == 'table') then out[k] = set; end
    end
    return out;
end

-- one of your sets by name, any case
mine.FindSet = function(name)
    local want = string.lower(name or '');
    for k, set in pairs(gVana.FileSets or {}) do
        if (type(set) == 'table') and (string.lower(k) == want) then return set, k; end
    end
    return nil;
end

-- Names players know from other swappers, for the built set that does that job. "Engaged" is
-- what LuAshitacast and GearSwap profiles call the fighting set.
mine.Aliases = { engaged = 'Tp_Default', tp = 'Tp_Default', melee = 'Tp_Default', engagedhybrid = 'Tp_Hybrid',
    hybrid = 'Tp_Hybrid', engagedacc = 'Tp_Acc', acc = 'Tp_Acc', ws = 'Ws_Default', weaponskill = 'Ws_Default',
    fastcast = 'Precast', midcast = 'Interim', rest = 'Resting', dt = 'Dt', pdt = 'Dt', kite = 'Movement', idledt = 'Dt' };

-- The name a window list shows a built set under.
mine.Label = function(name)
    if (name == 'Tp_Default') then return 'Engaged'; end
    if (name == 'Tp_Hybrid') then return 'Engaged (hybrid)'; end
    if (name == 'Tp_Acc') then return 'Engaged (accuracy)'; end
    return name;
end

-- A set's name as the handlers look it up: letters, digits and _ only, so "Call Beast" is the
-- set that goes on for Call Beast, the same as the built ones.
mine.SetName = function(name)
    name = string.gsub(name or '', '[^%w_]', '');
    name = mine.Aliases[string.lower(name)] or name;
    for k in pairs(gVana.Built or {}) do
        if (string.lower(k) == string.lower(name)) then return k; end
    end
    local _, existing = mine.FindSet(name);
    return existing or name;
end

local function changed()
    require('vg.core').ApplyOwn();
end

-- /vg save <name>: what you are wearing now, slot by slot, written into your profile as the set.
mine.SaveSet = function(name)
    name = mine.SetName(name);
    if (name == '') or (profilePath() == nil) then
        return false;
    end
    local set = {};
    local count = 0;
    local weapons = { Main = true, Sub = true };
    for slot, worn in pairs(gData.GetEquipment()) do
        -- Main/Sub only when the player allows their profile swaps (/vg weapons on): costs TP
        if (gVana.Prefs.Weapons) or (not weapons[slot]) then
            set[slot] = worn.Name;
            count = count + 1;
        end
    end
    local vp = require('vg.profile');
    vp.SaveSet(profilePath(), vp.FileName(name), set);
    changed();
    if (mine.Dressing) then mine.Dress(false); end
    return true, name, count;
end

-- /vg dress: vanagear stops swapping so the player can put on what they like from the equipment
-- menu; /vg save puts swapping back on.
mine.Dressing = false;
mine.Dress = function(on)
    mine.Dressing = on;
    for i = 1, 16 do gState.Disabled[i] = on; end
end

-- One slot of one of your sets, in your profile; nil takes it out so the built piece fills it.
mine.SetSlot = function(name, slot, piece)
    name = mine.SetName(name);
    if (name == '') or (profilePath() == nil) then
        return false;
    end
    local vp = require('vg.profile');
    vp.SetSlot(profilePath(), vp.FileName(name), slot, piece);
    changed();
    return true, name;
end

-- One of your sets out of your profile: a built one is back to what vanagear builds.
mine.RemoveSet = function(name)
    local vp = require('vg.profile');
    local _, found = mine.FindSet(vp.FileName(mine.SetName(name)));
    if (found == nil) then
        _, found = mine.FindSet(name);
    end
    if (found == nil) or (profilePath() == nil) then
        return false;
    end
    vp.SaveSet(profilePath(), found, false);
    changed();
    return true, found;
end

-- The name the set has in your file, for Undo: the set may be gone from the file, so not FindSet's.
local function undoName(name)
    local vp = require('vg.profile');
    local _, found = mine.FindSet(vp.FileName(mine.SetName(name)));
    return found or vp.FileName(mine.SetName(name));
end

mine.CanUndo = function(name)
    return (profilePath() ~= nil) and require('vg.profile').CanUndo(profilePath(), undoName(name));
end

-- Your last change to one of your sets taken back.
mine.Undo = function(name)
    if (profilePath() == nil) then
        return false;
    end
    local done = require('vg.profile').Undo(profilePath(), undoName(name));
    if (done) then changed(); end
    return done;
end

mine.ReloadSets = function()
    require('vg.core').ApplyOwn();
end

return mine;
