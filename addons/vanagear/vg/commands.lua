--[[
    vanagear: /vg, and /gs for players coming from Windower.

    Both are caught by the addon's own command event: no /alias is ever registered, because
    queuing aliases while a character logs in is what hung Ferrin's login on 28 Sept 2026.
]]--

local core = require('vg.core');
local gsnames = require('vg.gsnames');
local use = require('vg.use');
local mine = require('vg.mine');

local commands = {};

local function say(text)
    print(chat.header('vanagear') .. chat.message(text));
end

local function onOff(word, current)
    if (word == 'on') then return true; end
    if (word == 'off') then return false; end
    return not current;
end

local function job()
    return gVana.Who and gVana.Who.JobName or '';
end

local slotNames = {};
for i, name in ipairs(gData.Constants.EquipSlotNames) do
    slotNames[string.lower(name)] = i;
end

-- What /vg help lists: only the lines that mean something for the job, subjob and gear now.
commands.Lines = function()
    local s = gVana.Sets;
    local lines = {};
    local function line(cmd, text) lines[#lines + 1] = { cmd, text }; end
    line('/vg', 'open the window: your sets, the piece in each slot and why');
    line('/vg build', 'rebuild the sets now (also happens on job change, level up and new gear)');
    line('/vg status', 'what is running, and any piece your sets name that you do not carry');
    line('/vg dt [pdt | mdt | all | off]', 'damage taken gear over everything else: physical, magic or both (also in the Settings tab)');
    line('/vg kite', 'movement gear over everything else, on or off');
    if (s.TH ~= nil) then line('/vg th', 'treasure hunter gear over everything else, on or off'); end
    if (s.Tp_Default ~= nil) then line('/vg tp default | hybrid | acc | proc', 'which fighting set you wear (proc: your low-damage Tp_Proc and Ws_Proc)'); end
    if (s.Idle_Refresh ~= nil) then
        line('/vg idle auto | regen | refresh | speed', 'standing still: auto puts regen on below 60% HP and refresh below 70% MP');
    else
        line('/vg idle auto | regen | speed', 'standing still: auto puts regen on below 60% HP');
    end
    if (s.Nuke ~= nil) or (s.Enfeebling ~= nil) then line('/vg nuke power | acc', 'damage or accuracy for nukes and enfeebles'); end
    if (s.Burst ~= nil) then line('/vg burst', 'magic burst gear on nukes, on or off'); end
    if (s.Interim ~= nil) then line('/vg stage', 'precast, then safety gear while casting, then midcast gear just before the spell lands'); end
    if (s.PetTP ~= nil) then line('/vg pet tp | dt | acc', 'what your gear does for your pet while it fights'); end
    if (job() == 'BST') then
        line('/vg call <jug>', 'put the jug in your ammo slot and Call Beast (no jug: the last one you called)');
        line('/vg loyal <jug>', 'the same with Bestial Loyalty, which keeps the jug');
        line('/vg reward [food]', 'put the pet food in and Reward: alpha ... theta, debuff (Roborant, else Poultice); no food: your best biscuit');
        line('/vg jugs, /vg foods', 'the jugs and pet foods you carry');
    end
    line('/vg weapons on | off', 'allow Main/Sub swaps from your own profile (costs TP); weapons/ammo are never generated');
    line('/vg weapon main|sub|range <item>', 'Main/Sub: your pick in every set. Range: equip once; use /vg set for a saved action choice');
    line('/vg lock <slot>|all, /vg unlock <slot>|all', 'keep a slot as you have it: nothing equips it, saved for this job (/vg disable and /vg enable are the same)');
    line('/vg why <set> <slot>', 'the piece that won and the three behind it');
    line('/vg upgrades', 'pieces in storage that would win a slot');
    line('/vg underlay on | off', 'fill the slots your sets leave empty from the built sets (off: your sets exactly as written)');
    line('/vg reload, /vg load <file>, /vg newlua [name]', 'read your profile again / load another / make a new one, in config\\addons\\vanagear\\profiles');
    line('/vg template', 'write this job\'s profile again from its job template, keeping your sets and commands (the old one goes to backups first)');
    line('/vg edit, /vg list [set]', 'open your profile in your editor / list every set worn, built ones included (/vg sets lists only yours)');
    line('/vg set <set> [seconds], /vg equip <slot> <item>', 'keep a set on for a while / put one piece on');
    line('/vg fwd <args>', 'hand a command to your profile\'s own HandleCommand');
    line('/vg lockstyle <set>, /vg naked', 'the engine\'s look lock and strip');
    line('/vg gear, /vg packvalidate, /vg pack|unpack', 'Packer: fetch, check and put away the gear your profile names');
    line('/vg config, /vg debug, /vg exec <lua>', 'the engine\'s settings window, debug output, run a line of Lua');
    line('/vg trusts on | off', 'give your called trusts the stats of a set from your bags (a tab each in /vg); off stops sending');
    for word, p in pairs(use.Words()) do
        line('/vg ' .. word, 'equip ' .. p.Name .. ', wait it out, use it, give the slot back');
    end
    local own = mine.List();
    local words = {};
    for word in pairs(own) do words[#words + 1] = word; end
    table.sort(words);
    for _, word in ipairs(words) do
        line('/vg ' .. word, 'yours: ' .. table.concat(own[word].lines, '; '));
    end
    line('/vg add <word> <command>; <command>', 'make your own command for this job, run with /vg <word> or just /<word> (add "all" before the word for every job); "wait 2" pauses');
    line('then <command> / burst <command>', 'in a command: goes once the action before it has finished, plus the link gap / at once (a chain); /vg stop stops a running command');
    line('/vg with <set> <command>', 'put a set on, then the command (/ja, /ma, /pet, /ws), and keep the set on until it is done');
    line('/vg dress', 'stop swapping so you can put on what you like from the equipment menu');
    line('/vg save <name> (or /vg addset)', 'save what you are wearing as a set (swapping comes back on). Named like a built set (Idle, Tp_Default, Reward, a weaponskill) it replaces those slots there');
    line('/vg set <name> <slot> <item>', 'change one slot of a set (any set in the /vg window too); /vg sets lists yours');
    line('/vg wear <name> | off', 'put on one of your sets and keep it on over the built sets');
    line('/vg look <name> | off', 'lock a set as your look (/lockstyle), built or yours, kept per job; the swaps go on underneath without showing');
    line('/vg remove <word>, /vg folder', 'delete one of your commands or sets; open your profiles folder to edit by hand (then /vg reload)');
    line('/vg wait <seconds>', 'how long after zoning in before gear changes (protects the server)');
    line('/vg keys mote | off', 'Motenten\'s F9-F12 keys, as GearSwap players have them');
    line('/gs c <word>', 'the same as /vg <word>, so GearSwap macros keep working');
    line('/gs equip <set>', 'put on a set by its GearSwap or vanagear name');
    return lines;
end

local function help()
    say(string.format('Commands for %s%s/%s:', job(), gVana.Who and gVana.Who.Level or '', gVana.Who and gVana.Who.SubName or ''));
    for _, l in ipairs(commands.Lines()) do
        print(chat.header('vanagear') .. chat.color1(2, l[1]) .. chat.message('  ' .. l[2]));
    end
end

-- the set a name means: a built one, the profile's own, or a GearSwap name for either
local function findSet(name)
    if (gVana.Sets[name] ~= nil) then return gVana.Sets[name], name; end
    local own, ownName = mine.FindSet(name);
    if (own ~= nil) then return own, ownName; end
    if (gProfile ~= nil) and (type(gProfile.Sets) == 'table') and (gProfile.Sets[name] ~= nil) then
        return gProfile.Sets[name], name;
    end
    local mapped = gsnames.ToVanagear(name);
    if (mapped ~= nil) then
        if (gProfile ~= nil) and (type(gProfile.Sets) == 'table') then
            for k, set in pairs(gProfile.Sets) do
                if (gsnames.ToVanagear(k) == mapped) or (k == mapped) then return set, k; end
            end
        end
        if (gVana.Sets[mapped] ~= nil) then return gVana.Sets[mapped], mapped; end
    end
    local lower = string.lower(name);
    for k, set in pairs(gVana.Sets) do
        if (string.lower(k) == lower) then return set, k; end
    end
    return nil;
end

local function why(setName, slotName)
    local set, name = findSet(setName or '');
    local ex = name and gVana.Explain[name];
    local slot = slotNames[string.lower(slotName or '')];
    if (ex == nil) or (slot == nil) then
        say('Usage: /vg why <set> <slot>, for example /vg why Tp_Default Ring1');
        return;
    end
    if (gVana.Held[slot] ~= nil) then
        say(gData.Constants.EquipSlotNames[slot] .. ': ' .. gVana.Held[slot] .. '.');
        return;
    end
    local ranks = ex.ranks[slot] or {};
    if (#ranks == 0) then
        say(name .. ' ' .. gData.Constants.EquipSlotNames[slot] .. ': nothing you carry adds to this set in this slot.');
        return;
    end
    say(string.format('%s %s:', name, gData.Constants.EquipSlotNames[slot]));
    for i = 1, math.min(4, #ranks) do
        local r = ranks[i];
        print(chat.header('vanagear') .. chat.message(string.format('  %d. %s  +%.1f', i, r.c.piece.Name, r.gain)));
    end
end

local function upgrades()
    local bags = require('vg.bags');
    local score = require('vg.score');
    local stored = bags.ReadStorage();
    if (#stored == 0) then
        say('Nothing to compare: the Mog Safe and Storage can only be read in the Mog House or near a Nomad Moogle.');
        return;
    end
    local found = 0;
    for name, ex in pairs(gVana.Explain) do
        for slot, ranks in pairs(ex.ranks) do
            local bestGain = (ranks[1] ~= nil) and ranks[1].gain or 0;
            for _, p in ipairs(stored) do
                if (bags.CanWear(p, slot, gVana.Who, true)) then
                    local v = score.Vector(p, slot, ex.mode, require('vg.data').PetTypes[job()] or { 0 });
                    local alone = score.Total(ex.mode, v);
                    if (alone > bestGain + 0.5) then
                        found = found + 1;
                        if (found <= 15) then
                            say(string.format('%s (bag %d) would go in %s %s.', p.Name, p.Bag, name, gData.Constants.EquipSlotNames[slot]));
                        end
                        break;
                    end
                end
            end
        end
    end
    if (found == 0) then
        say('Nothing in storage beats what you carry.');
    elseif (found > 15) then
        say(string.format('...and %d more. Move them to a wardrobe and the sets pick them up.', found - 15));
    end
end

local function save()
    core.SavePrefs();
end

-- /vg status: what is running and why gear did or did not change.
commands.Status = function()
    local count = 0;
    for _ in pairs(gVana.Sets) do count = count + 1; end
    say(string.format('Waiting after zone-in: %s.', core.Holding() and 'yes, nothing is equipped yet' or 'no'));
    if (gProfile == nil) then
        say('No profile loaded.');
    else
        say(string.format('Profile: %s. Built sets under it: %s.', tostring(gProfile.FileName),
            core.Underlay() and 'on' or 'off (used only where it names gear you do not carry)'));
    end
    if (gVana.Who ~= nil) then
        say(string.format('Built %d sets for %s%d/%s from %d pieces you carry.', count, gVana.Who.JobName, gVana.Who.Level,
            gVana.Who.SubName, #gVana.Pieces));
    else
        say('No sets built yet.');
    end
    local missing = gVana.LastMissing or {};
    if (#missing > 0) then
        say('Last gear change asked for pieces not in your inventory or wardrobes: ' .. table.concat(missing, ', '));
    elseif (gVana.LastEquipAt ~= nil) then
        say('Last gear change found every piece it asked for.');
    else
        say('No gear change has been asked for yet.');
    end
end

-- /gs validate: pieces the profile's sets name that are not in a bag you can equip from.
commands.Validate = function()
    if (gProfile == nil) or (type(gProfile.Sets) ~= 'table') then
        say('No profile loaded.');
        return;
    end
    local have = {};
    for _, p in ipairs(gVana.Pieces) do have[string.lower(p.Name)] = true; end
    local missing = {};
    for setName, set in pairs(gProfile.Sets) do
        if (type(set) == 'table') then
            for slot, entry in pairs(set) do
                local name = (type(entry) == 'table') and entry.Name or entry;
                if (type(name) == 'string') and (name ~= 'remove') and (name ~= 'displaced') and (not have[string.lower(name)]) then
                    missing[name] = missing[name] or {};
                    table.insert(missing[name], setName);
                end
            end
        end
    end
    local count = 0;
    for name, where in pairs(missing) do
        count = count + 1;
        say(string.format('%s: not in your inventory or wardrobes (%s)', name, table.concat(where, ', ')));
    end
    if (count == 0) then
        say('Every piece your sets name is in a bag you can equip from.');
    end
end

-- Motenten's F9-F12 keys, bound to the same /gs c commands GearSwap players already use
-- (Mote-Globals.lua binds). Off until the player asks; bound once, after the zone wait, one
-- command at a time.
commands.MoteKeys = {
    { 'F9', '/gs c cycle OffenseMode' },
    { '^F9', '/gs c cycle HybridMode' },
    { '!F9', '/gs c cycle RangedMode' },
    { '@F9', '/gs c cycle WeaponskillMode' },
    { 'F10', '/gs c set DefenseMode Physical' },
    { '^F10', '/gs c cycle PhysicalDefenseMode' },
    { '!F10', '/gs c toggle Kiting' },
    { 'F11', '/gs c set DefenseMode Magical' },
    { '^F11', '/gs c cycle CastingMode' },
    { 'F12', '/gs c update user' },
    { '^F12', '/gs c cycle IdleMode' },
    { '!F12', '/gs c reset DefenseMode' },
};

commands.Vg = function(args)
    local t = gVana.Toggles;
    local p = gVana.Prefs;
    local cmd = string.lower(args[1] or '');
    local arg = string.lower(args[2] or '');

    -- The engine's commands (LuAshitacast's, carried as vanagear's own in commandhandlers.lua), typed as
    -- /vg <command>. Where a word is vanagear's too, the shape decides: /vg set <set> [seconds] locks a set
    -- on and /vg set <set> <slot> <item> changes your set; /vg equip <slot> <item> is the engine's and
    -- /vg equip <set> vanagear's. The engine's Packer validate is /vg packvalidate; /vg config opens its settings.
    -- addset, disable and enable are vanagear's own now (/vg save, /vg lock, /vg unlock), one way each.
    local engineWords = { debug = true, edit = true, fwd = true, gear = true,
        list = true, load = true, lockstyle = true, naked = true, newlua = true, pack = true, unpack = true, preppack = true,
        prepunpack = true, safecall = true, unload = true, exec = true };
    local engine = nil;
    if (engineWords[cmd]) then engine = cmd;
    elseif (cmd == 'packvalidate') then engine = 'validate';
    elseif (cmd == 'config') then engine = '';
    elseif (cmd == 'set') and (args[2] ~= nil) and (slotNames[string.lower(args[3] or '')] == nil) then engine = 'set';
    elseif (cmd == 'equip') and (args[3] ~= nil) and (slotNames[arg] ~= nil) then engine = 'equip';
    end
    if (engine ~= nil) then
        local rest;
        local raw = commands.Raw;
        if (raw ~= nil) then
            -- as typed, so quoted item names keep their spaces: drop "/vg" and the word
            rest = string.match(raw, '^%S+%s+%S+%s*(.*)$') or '';
        else
            rest = table.concat(args, ' ', 2);
        end
        local line = (engine == '') and '/vg' or ('/vg ' .. engine .. ((rest ~= '') and (' ' .. rest) or ''));
        gCommandHandlers.HandleCommand({ command = line, blocked = false });
        return;
    end

    if (cmd == '') then
        gVana.Window.Open = not gVana.Window.Open;
    elseif (cmd == 'help') or (cmd == 'commands') then
        help();
    elseif (cmd == 'status') then
        commands.Status();
    elseif (cmd == 'reload') then
        -- the profile file read again (as the engine's reload did), then the sets rebuilt into it
        if (gProfile ~= nil) and (gProfile.FilePath ~= nil) then
            gState.LoadProfileEx(gProfile.FilePath);
        end
        mine.Reload();   -- your commands and the every-job ALL.lua read again too
        core.Request(true);
        say('Reloaded your profile; rebuilding.');
    elseif (cmd == 'template') then
        -- this job's profile written again from its template, your sets and commands kept, a backup first
        local job = (gProfile ~= nil) and gProfile.VgJob or (gVana.Who and gVana.Who.JobName);
        if (job == nil) then
            say('No job loaded yet.');
        else
            local saved, why = require('vg.profile').Retemplate(job);
            if (saved == nil) then
                say('Not rewritten: ' .. why .. '.');
            else
                gState.LoadProfileEx(require('vg.profile').Path(job));
                core.Request(true);
                say('Your ' .. job .. ' profile is the ' .. job .. ' template now, with your sets and commands; rebuilding. The old one is at ' .. saved .. '.');
            end
        end
    elseif (cmd == 'build') then
        core.Request(true);
        say('Rebuilding.');
    elseif (cmd == 'dt') then
        -- on (all), pdt, mdt or off; no word turns it on or off
        if (arg == 'pdt') or (arg == 'mdt') then t.DT = arg;
        elseif (arg == 'all') then t.DT = true;
        else t.DT = onOff(arg, t.DT and true or false); end
        say('Damage taken gear: ' .. ((t.DT == 'pdt') and 'physical (Dt_Physical)' or ((t.DT == 'mdt') and 'magic (Dt_Magic)' or (t.DT and 'all (Dt)' or 'off'))) .. '.');
    elseif (cmd == 'kite') then
        t.Kite = onOff(arg, t.Kite); say('Movement gear ' .. (t.Kite and 'on' or 'off') .. '.');
    elseif (cmd == 'th') then
        t.TH = onOff(arg, t.TH); say('Treasure hunter gear ' .. (t.TH and 'on' or 'off') .. '.');
    elseif (cmd == 'burst') then
        t.Burst = onOff(arg, t.Burst); say('Magic burst gear ' .. (t.Burst and 'on' or 'off') .. '.');
    elseif (cmd == 'stage') then
        t.Stage = onOff(arg, t.Stage); say('Staged casting ' .. (t.Stage and 'on' or 'off') .. '.');
    elseif (cmd == 'tp') then
        -- proc: low-damage sets of your own (Tp_Proc, Ws_Proc) for a weakness you have to trigger, as in
        -- Vagary (GetAwayCoxn's SAM.lua); not in the cycle, only by name
        local modes = { default = 'Default', normal = 'Default', hybrid = 'Hybrid', acc = 'Acc', proc = 'Proc' };
        if (modes[arg] ~= nil) then t.Tp = modes[arg];
        else
            local order = { Default = 'Hybrid', Hybrid = 'Acc', Acc = 'Default' };
            t.Tp = order[t.Tp] or 'Default';
        end
        say('Fighting set: Tp_' .. t.Tp .. '.');
    elseif (cmd == 'idle') then
        local modes = { auto = true, regen = true, refresh = true, speed = true };
        t.Idle = modes[arg] and arg or 'auto';
        say('Idle: ' .. t.Idle .. '.');
    elseif (cmd == 'nuke') then
        t.Nuke = (arg == 'acc') and 'acc' or ((arg == 'power') and 'power' or ((t.Nuke == 'acc') and 'power' or 'acc'));
        say('Nukes and enfeebles: ' .. t.Nuke .. '.');
    elseif (cmd == 'pet') then
        local modes = { tp = true, dt = true, acc = true };
        t.Pet = modes[arg] and arg or 'tp';
        say('Pet gear: ' .. t.Pet .. '.');
    elseif (cmd == 'weapons') then
        p.Weapons = onOff(arg, p.Weapons); save(); core.Request();
        say('Main/Sub profile swaps ' .. (p.Weapons and 'allowed' or 'protected') .. '. Weapons/ammo are never chosen automatically.');
    elseif (cmd == 'trusts') then
        p.TrustGear = onOff(arg, p.TrustGear ~= false); save();
        say('Trust gear: ' .. (p.TrustGear and 'your called trusts get their sets' or 'nothing is sent to your trusts') .. '.');
    elseif (cmd == 'weapon') then
        local slotName = ({ main = 'Main', sub = 'Sub', range = 'Range' })[arg];
        local piece = table.concat(args, ' ', 3);
        if (slotName == nil) or (piece == '') then
            say('Usage: /vg weapon main|sub|range <item> (or "none" to leave the slot alone, "remove" to take it off)');
        else
            local lower = string.lower(piece);
            core.SetWeaponPick(slotName, (lower == 'none') and nil or ((lower == 'remove') and 'remove' or piece));
            say(slotName .. ((lower == 'none') and ' is left alone.' or (': ' .. piece .. ((slotName == 'Range') and ', equipped once.' or ', in every set.'))));
        end
    elseif (cmd == 'lock') or (cmd == 'unlock') or (cmd == 'disable') or (cmd == 'enable') then
        -- one lock: /vg disable and /vg enable (LuAshitacast's words) are /vg lock and /vg unlock
        local on = (cmd == 'lock') or (cmd == 'disable');
        local slot = (arg == '' or arg == 'all') and 'all' or slotNames[arg];
        if (slot == nil) then say('Usage: /vg lock <slot> (or all), for example /vg lock Neck'); return; end
        core.SetLock(slot, on);
        say(((slot == 'all') and 'Every slot' or gData.Constants.EquipSlotNames[slot]) .. (on and ' locked for ' or ' unlocked for ') .. job() .. '.');
    elseif (cmd == 'why') then
        why(args[2], args[3]);
    elseif (cmd == 'upgrades') then
        upgrades();
    elseif (cmd == 'underlay') then
        p.Underlay[job()] = onOff(arg, core.Underlay());
        save();
        say('Built sets under your own profile for ' .. job() .. ': ' .. (p.Underlay[job()] and 'on' or 'off') .. '.');
    elseif (cmd == 'keys') then
        p.MoteKeys = (arg == 'mote') or ((arg ~= 'off') and (not p.MoteKeys));
        save();
        core.QueueKeys(p.MoteKeys);
        say('Motenten\'s F9-F12 keys ' .. (p.MoteKeys and 'bound' or 'unbound') .. '.');
    elseif (cmd == 'stop') then
        require('vg.mine').Stop();
    elseif (cmd == 'wait') then
        local n = tonumber(args[2]);
        if (n ~= nil) and (n >= 0) then p.ZoneWait = n; save(); end
        say('Zone wait: ' .. tostring(p.ZoneWait) .. ' seconds after you are standing in the zone.');
    elseif (cmd == 'call') or (cmd == 'loyal') or (cmd == 'loyalty') or (cmd == 'bl') then
        use.Call((cmd == 'call') and 'Call Beast' or 'Bestial Loyalty', table.concat(args, ' ', 2));
    elseif (cmd == 'reward') then
        use.Reward(table.concat(args, ' ', 2));
    elseif (cmd == 'jugs') or (cmd == 'foods') then
        local list = (cmd == 'jugs') and use.Jugs() or use.Foods();
        if (#list == 0) then say('You carry no ' .. ((cmd == 'jugs') and 'jugs.' or 'pet food.')); end
        for _, piece in ipairs(list) do
            say(string.format('%s (level %d)', piece.Name, piece.Server.level));
        end
    elseif (cmd == 'add') then
        local everyJob = (arg == 'all');
        local word = args[everyJob and 3 or 2];
        local text = table.concat(args, ' ', everyJob and 4 or 3);
        local ok, saved, file = mine.Add(word, mine.Split(text), everyJob);
        if (ok) then
            say(string.format('/vg %s saved (%s.lua).', saved, file));
        else
            say('Usage: /vg add <word> <command>; <command>, for example /vg add sheep /vg call wormy; wait 3; /ja "Sheep Song" <t>');
        end
    elseif (cmd == 'remove') or (cmd == 'delete') then
        local name = table.concat(args, ' ', 2);
        local ok, file = mine.Remove(arg);
        if (ok) then
            say(string.format('/vg %s removed (%s.lua).', arg, file));
        elseif (mine.RemoveSet(name)) then
            say('Your set ' .. name .. ' removed.');
        else
            say('You have no command or set called ' .. name .. '.');
        end
    elseif (cmd == 'with') then
        -- the command is taken from the raw text, so its quotes survive
        local raw = commands.Raw or table.concat(args, ' ');
        local setName, command = string.match(raw, '^%S+%s+[Ww][Ii][Tt][Hh]%s+(%S+)%s+(/.+)$');
        if (setName == nil) then
            setName, command = string.match(raw, '^[Ww][Ii][Tt][Hh]%s+(%S+)%s+(/.+)$');
        end
        local set, found = findSet(setName or '');
        if (command == nil) or (set == nil) then
            say('Usage: /vg with <set> <command>, for example /vg with lizard /ja "Bestial Loyalty" <me>');
        else
            use.With(found, set, command);
        end
    elseif (cmd == 'dress') then
        mine.Dress(arg ~= 'off');
        say(mine.Dressing and 'Swapping paused: put on what you like, then /vg save <name>. /vg dress off to stop.'
            or 'Swapping back on.');
    elseif (cmd == 'sets') then
        local names = {};
        for k, set in pairs(mine.Sets()) do
            local n = 0;
            for _ in pairs(set) do n = n + 1; end
            names[#names + 1] = string.format('%s (%d)', k, n);
        end
        table.sort(names);
        say('Your sets: ' .. ((#names > 0) and table.concat(names, ', ') or 'none yet. /vg dress, put gear on, /vg save <name>.'));
    elseif (cmd == 'set') then
        local slot = slotNames[string.lower(args[3] or '')];
        local piece = table.concat(args, ' ', 4);
        if (args[2] == nil) or (slot == nil) or (piece == '') then
            say('Usage: /vg set <name> <slot> <item>, for example /vg set Reward Body Totemic Jackcoat +1 (item "built" gives the slot back)');
        else
            local slotName = gData.Constants.EquipSlotNames[slot];
            local _, name = mine.SetSlot(args[2], slotName, (string.lower(piece) ~= 'built') and piece or nil);
            say(string.format('%s %s: %s.', name, slotName, (string.lower(piece) ~= 'built') and piece or 'built piece'));
        end
    elseif (cmd == 'save') or (cmd == 'addset') then
        -- one way to save what you wear: /vg addset (LuAshitacast's word) is /vg save
        local ok, name, count = mine.SaveSet(table.concat(args, ' ', 2));
        say(ok and string.format('Saved %s: the %d pieces you are wearing.', name, count) or 'Usage: /vg save <name>');
    elseif (cmd == 'wear') or (cmd == 'look') then
        local name = table.concat(args, ' ', 2);
        if (arg == 'off') then
            if (cmd == 'wear') then use.Wear(nil); say('Back to the built sets.');
            else core.SetLook(nil); say('Look unlocked.'); end
            return;
        end
        local set, found = findSet(name);
        if (set == nil) and (cmd == 'look') then
            -- any set vanagear has can be the look, built ones too
            for k in pairs(gVana.Sets) do
                if (string.lower(k) == string.lower(mine.SetName(name))) or (string.lower(k) == string.lower(name)) then found = k; set = gVana.Sets[k]; end
            end
            if (set ~= nil) then
                core.SetLook(found);
                say(mine.Label(found) .. ' locked as your look. /vg look off to unlock.');
                return;
            end
        end
        if (set == nil) then
            local names = {};
            for k in pairs(mine.Sets()) do names[#names + 1] = k; end
            table.sort(names);
            say(string.format('No set called %s. Yours: %s.', name, (#names > 0) and table.concat(names, ', ') or 'none yet (/vg save <name>)'));
        elseif (cmd == 'wear') then
            use.Wear(found, set);
            say('Wearing ' .. found .. ' until /vg wear off.');
        else
            core.SetLook(found);
            say(found .. ' locked as your look. /vg look off to unlock.');
        end
    elseif (cmd == 'folder') then
        say('Your profiles, with your sets and commands in them, are in ' .. mine.OpenFolder() .. '. After editing one by hand, type /vg reload.');
    elseif (cmd == 'mine') then
        -- /vg mine reload is /vg reload: your commands live in the profile, read again with it
        if (arg == 'reload') then commands.Vg({ 'reload' }); end
        gVana.Window.Open = true;
        gVana.Window.Mine = true;
    elseif (mine.Run(cmd)) then
        -- one of the player's own commands
    elseif (use.Start(cmd)) then
        -- a usable ring or earring
    elseif (gProfile ~= nil) then
        -- anything else is the profile's own command, as /lac fwd would send it
        gState.SafeCall('HandleCommand', args);
    end
end

-- /gs: GearSwap's commands.
commands.Gs = function(args)
    local cmd = string.lower(args[1] or '');
    if (cmd == 'c') then
        local rest = {};
        for i = 2, #args do rest[#rest + 1] = args[i]; end
        -- Mote's mode commands become vanagear's
        local first = string.lower(rest[1] or '');
        if (first == 'cycle') or (first == 'set') then
            local mode = string.lower(rest[2] or '');
            local value = rest[3];
            if (mode == 'offensemode') or (mode == 'hybridmode') then
                commands.Vg({ 'tp', value or '' });
            elseif (mode == 'idlemode') then
                commands.Vg({ 'idle', value or '' });
            elseif (mode == 'castingmode') then
                commands.Vg({ 'nuke', (value and string.lower(value) ~= 'normal') and 'acc' or '' });
            elseif (mode == 'weaponskillmode') or (mode == 'rangedmode') then
                commands.Vg({ 'tp', value or '' });
            elseif (mode == 'defensemode') or (mode == 'physicaldefensemode') or (mode == 'magicaldefensemode') then
                commands.Vg({ 'dt', ((value ~= nil) and (string.lower(value) == 'none')) and 'off' or 'on' });
            else
                commands.Vg(rest);
            end
            return;
        end
        if (first == 'toggle') and (string.lower(rest[2] or '') == 'kiting') then
            commands.Vg({ 'kite' });
            return;
        end
        if (first == 'reset') and (string.lower(rest[2] or '') == 'defensemode') then
            commands.Vg({ 'dt', 'off' });
            return;
        end
        if (first == 'update') then
            say('Sets are current.');
            return;
        end
        commands.Vg(rest);
    elseif (cmd == 'equip') then
        local name = table.concat(args, ' ', 2);
        local set, found = findSet(name);
        if (set == nil) then
            say('No set called ' .. name .. '.');
        else
            gFunc.ForceEquipSet(set);
            say('Wearing ' .. found .. '.');
        end
    elseif (cmd == 'validate') then
        commands.Validate();
    elseif (cmd == 'export') then
        commands.Vg({ 'save', args[2] or 'Exported' });
    elseif (cmd == 'reload') or (cmd == 'r') then
        commands.Vg({ 'reload' });
    else
        commands.Vg(args);
    end
end

-- Called from the command event: /vg, /gs and your own words typed straight. Returns true if the
-- command was ours.
commands.Handle = function(e)
    local args = e.command:args();
    if (#args == 0) then
        return false;
    end
    local word = string.lower(args[1]);
    if (word ~= '/vg') and (word ~= '/vanagear') and (word ~= '/gs') and (word ~= '/gearswap') then
        -- one of the player's own commands typed straight: /lizard
        if (string.sub(word, 1, 1) == '/') and (#args == 1) and (mine.List()[string.sub(word, 2)] ~= nil) then
            e.blocked = true;
            mine.Run(string.sub(word, 2));
            return true;
        end
        return false;
    end
    e.blocked = true;
    commands.Raw = e.command;
    local rest = {};
    for i = 2, #args do rest[#rest + 1] = args[i]; end
    if (word == '/gs') or (word == '/gearswap') then
        commands.Gs(rest);
    else
        commands.Vg(rest);
    end
    commands.Raw = nil;
    return true;
end

return commands;
