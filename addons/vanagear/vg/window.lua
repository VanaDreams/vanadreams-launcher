--[[
    vanagear: the /vg window. The sets built for the job you are on, the piece in each slot and
    what it scored, and the commands that mean something for this job. Every slot can be changed
    here: the piece you pick is yours (vg\mine.lua) and replaces the built one in that set, and
    New set makes a set of your own.
]]--

local locale = require('vg.i18n');
local imgui = locale.ImGui(require('imgui'));
local vd = locale.Style(require('vdstyle'));

local window = { Open = false, Selected = nil };
gVana.Window = window;

local isOpen = { false };
local newWord = { '' };
local newLines = { '' };
local everyJob = { false };
local mineNote = nil;
local newSet = { '' };
local setNote = nil;

local function sayNote(text) mineNote = text; end
local renderSlots;

-- The "Your commands" tab: one word that runs several game commands in order. Laid out as
-- numbered steps with examples to fill the form from (Ferrin, 18 Oct 2026: "the write your own
-- lines is confusing", "a tab of the whole screen with instructions and examples").
local examples = {
    { word = 'pull', lines = '/ja "Provoke" <t>', every = false,
      what = 'Provoke whatever you have targeted.' },
    { word = 'buffs', lines = '/ma "Haste" <me>\nwait 4\n/ma "Refresh" <me>', every = false,
      what = 'Cast Haste on yourself, wait 4 seconds, then Refresh.' },
    { word = 'warpout', lines = '/vg warp', every = true,
      what = 'Put on your Warp Ring, use it and give the slot back (any job).' },
    { word = 'lizard', lines = '/vg with lizard /ja "Bestial Loyalty" <me>', every = false,
      what = 'BST: wear your set called lizard (the jug) and use Bestial Loyalty.' },
    { word = 'chain', lines = '/pet "Brain Crush" <t>\nthen /pet "Tail Blow" <t>\nthen /ws "Cloudsplitter" <t>\nburst /pet "Fireball" <t>', every = false,
      what = 'BST skillchain: each step goes when the one before has landed; the burst goes at once.' },
    { word = 'sc', lines = '/ws "Savage Blade" <t>\nthen /ws "Savage Blade" <t>', every = true,
      what = 'Two weaponskills, the second as soon as the first has finished and the link gap has passed.' },
};

-- The "How to" tab (M402): what a command word's lines can do, chains, and for people who want
-- Lua, what a LuAshitacast profile can do on top. Plain text, no form.
local function renderHowTo()
    vd.Eyebrow('How to: your commands, chains and going further');
    imgui.TextWrapped('Everything here is done in the Your commands tab, which writes your commands into your job\'s profile '
        .. '(config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua, its commands table; ALL.lua beside it for every job). '
        .. 'You never need Lua for a chain. Every /vg command is listed in the Your sets tab, under Commands.');
    imgui.Separator();

    vd.Eyebrow('1. A command word');
    imgui.TextWrapped('A word you choose runs its lines in order. Any game command is a line: /ja "Provoke" <t>, '
        .. '/ma "Cure IV" <stpc>, /ws "Savage Blade" <t>, /pet "Tail Blow" <t>, /vg wear Idle. Type /vg <word> or just /<word>.');
    imgui.BulletText('wait 3          pause three seconds before the next line');
    imgui.BulletText('/vg with <set> <command>   put one of your sets on, send the command, keep the set on until it lands');
    imgui.BulletText('/vg wear <set>  put one of your sets on and leave it on (/vg wear off)');
    imgui.Separator();

    vd.Eyebrow('2. A chain: steps that wait for each other');
    imgui.TextWrapped('A line that starts with "then" does not go until the action on the line before it has been seen to '
        .. 'start and then finish, plus the gap the server wants between links (3.3 seconds). A line that starts with '
        .. '"burst" goes the moment it finishes, with no gap. That is how a skillchain is timed without guessing waits.');
    imgui.Text('    /pet "Brain Crush" <t>');
    imgui.Text('    then /pet "Tail Blow" <t>');
    imgui.Text('    then /ws "Cloudsplitter" <t>');
    imgui.Text('    burst /pet "Fireball" <t>');
    imgui.TextWrapped('Brain Crush, then Tail Blow once it has landed (Fusion), then Cloudsplitter once that has landed '
        .. '(Light), then Fireball the moment Cloudsplitter finishes (the burst). The lines "then" can wait on are '
        .. '/ws, /ja, /ma and /pet. If a step never starts (no TP, no charges, out of range) or the target dies or '
        .. 'changes, the command stops and says why. /vg stop stops one by hand.');
    imgui.TextWrapped('What it does not do: it does not check TP, Ready charges or finishing moves before it starts. '
        .. 'Start it when you have them.');
    imgui.Separator();

    vd.Eyebrow('3. Your own sets in a command');
    imgui.TextWrapped('Make a set in the Your sets tab (or /vg save <name> from what you wear), then "/vg with <name> <command>" '
        .. 'wears it for that one action. For Beastmaster: /vg call <jug> and /vg loyal <jug> put the jug in the ammo '
        .. 'slot and call it; /vg jugs lists the ones you carry.');
    imgui.Separator();

    vd.Eyebrow('4. Going further: your profile in Lua');
    imgui.TextWrapped('Each job runs a profile file at config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua (/vg edit opens it). '
        .. 'It holds the built sets (rewritten at each rebuild), your sets (what the window and /vg addset write), your commands, '
        .. 'and these functions, each called at that moment:');
    imgui.BulletText('OnLoad / OnUnload          when the profile starts and stops');
    imgui.BulletText('HandleDefault              every time nothing is in flight: idle, engaged, resting; chains tick here');
    imgui.BulletText('HandleCommand(args)        /vg fwd <word> ... reaches here: your own commands in Lua');
    imgui.BulletText('HandlePrecast / HandleMidcast   a spell: gear before it starts, gear while it casts');
    imgui.BulletText('HandleWeaponskill / HandleAbility / HandleItem   a weaponskill, job ability or item, as it goes off');
    imgui.BulletText('HandlePreshot / HandleMidshot   a ranged attack, before and during');
    imgui.TextWrapped('Inside them: gFunc.Equip(slot, item), gFunc.EquipSet(set), gFunc.ForceEquip, gFunc.LockSet, '
        .. 'gFunc.Disable/Enable(slot), gFunc.Message(text); and gData.GetPlayer() (job, level, TP, HPP), '
        .. 'gData.GetTarget(), gData.GetPet(), gData.GetAction() and gData.GetPetAction() (what is going off right now, '
        .. 'nil when nothing is), gData.GetBuffCount(id), gData.GetEquipment(), gData.GetAugment(). '
        .. 'A chain in Lua is a step list, a send, then watching GetAction until it comes back nil before the next send; '
        .. 'the "then" line above is exactly that, done for you.');
    imgui.TextWrapped('AshitaCore:GetChatManager():QueueCommand(1, "/ws \\"Savage Blade\\" <t>") sends any command from Lua.');
end

local function renderMine()
    local mine = require('vg.mine');
    vd.Eyebrow('Your commands: one word that runs several game commands, in order');
    imgui.TextWrapped('Make your own short command. You pick a word, then write the game commands it runs, one per line. '
        .. 'Afterwards typing /vg <word> (or just /<word>) runs them for you.');
    imgui.Separator();

    imgui.Text('1. The word you will type');
    imgui.SameLine(260);
    imgui.PushItemWidth(160);
    imgui.InputText('##vg_mine_word', newWord, 32);
    imgui.PopItemWidth();
    imgui.SameLine();
    imgui.TextDisabled(newWord[1] ~= '' and ('you will type /vg ' .. newWord[1]) or 'for example: pull');

    imgui.Text('2. The commands it runs, one per line');
    imgui.InputTextMultiline('##vg_mine_lines', newLines, 1024, { -1, 90 });
    imgui.TextDisabled('Any game command works: /ja "Provoke" <t>, /ma "Cure IV" <stpc>, /vg wear Idle. '
        .. 'A line "wait 3" waits 3 seconds before the next line. <t> is your target, <me> is you.');
    imgui.TextDisabled('A line "then /ws ..." goes once the action before it has finished (plus the link gap); '
        .. '"burst /pet ..." goes the moment it finishes. The How to tab explains chains.');

    imgui.Text('3. Which jobs can use it');
    imgui.SameLine(260);
    imgui.Checkbox('every job (leave it off for ' .. ((gVana.Who and gVana.Who.JobName) or 'this job') .. ' only)##vg_mine_all', everyJob);

    if (vd.Button('4. Save it', nil, true)) then
        local ok, word, file = mine.Add(newWord[1], mine.Split(newLines[1]), everyJob[1]);
        if (ok) then
            sayNote(string.format('Saved. Type /vg %s to run it (kept in %s). It is listed in the Your sets tab under Commands.', word,
                (file == 'ALL') and 'ALL.lua, for every job' or ('your ' .. file .. ' profile')));
            newWord[1] = '';
            newLines[1] = '';
        else
            sayNote('Give it a word (step 1) and at least one command (step 2).');
        end
    end
    if (mineNote ~= nil) then
        imgui.SameLine();
        imgui.TextDisabled(mineNote);
    end

    imgui.Separator();
    vd.Eyebrow('Examples');
    imgui.TextDisabled('Press Use to put an example in the boxes above, change it if you like, then Save.');
    for i, ex in ipairs(examples) do
        if (vd.SmallButton('Use##vg_ex_' .. i)) then
            newWord[1] = ex.word;
            newLines[1] = ex.lines;
            everyJob[1] = ex.every;
        end
        imgui.SameLine();
        imgui.Text('/vg ' .. ex.word);
        imgui.SameLine(170);
        imgui.TextDisabled(ex.what);
    end

    imgui.Separator();
    vd.Eyebrow('Your saved commands');
    local list = mine.List();
    local words = {};
    for word in pairs(list) do words[#words + 1] = word; end
    table.sort(words);
    if (#words == 0) then
        imgui.TextDisabled('None yet.');
    end
    for _, word in ipairs(words) do
        local entry = list[word];
        if (vd.SmallButton('Run##run_' .. word)) then
            mine.Run(word);
        end
        imgui.SameLine();
        if (vd.SmallButton('Change##edit_' .. word)) then
            newWord[1] = word;
            newLines[1] = table.concat(entry.lines, '\n');
            everyJob[1] = (entry.file == 'ALL');
        end
        imgui.SameLine();
        if (vd.SmallButton('Delete##del_' .. word)) then
            mine.Remove(word);
        end
        imgui.SameLine();
        imgui.Text('/vg ' .. word);
        imgui.SameLine();
        imgui.TextDisabled(table.concat(entry.lines, '  then  ') .. ((entry.file == 'ALL') and '   (every job)' or ''));
    end
    imgui.Separator();
    if (vd.Button('Open the folder')) then
        mine.OpenFolder();
    end
    imgui.SameLine();
    if (vd.Button('Read your profile again')) then
        require('vg.commands').Vg({ 'reload' });
        sayNote('Read your profile again.');
    end
    imgui.SameLine();
    imgui.TextDisabled('Your commands are in your job\'s profile (its commands table), if you would rather write them by hand.');
end

-- The "Settings" tab (Lee, 24 Oct 2026: "some of the commands can get a setting in the vg tab ... as a
-- check box rather than a / command"). Each control runs the /vg command a macro would, so the two can
-- never disagree, and the commands stay for macros in a fight. Under them, the job template's own
-- toggles (its profile.State): a true/false as a checkbox, a choice (profile.StateChoices) as buttons.
local waitBuffer = { 10 };
local function choice(label, id, current, options)
    imgui.Text(label);
    imgui.SameLine(190);
    for i, o in ipairs(options) do
        if (i > 1) then imgui.SameLine(); end
        if (imgui.RadioButton(o[2] .. '##vg_set_' .. id .. i, current == o[1])) and (current ~= o[1]) then
            return o[1];
        end
    end
    return nil;
end
local function tick(label, id, on)
    local box = { on and true or false };
    if (imgui.Checkbox(label .. '##vg_set_' .. id, box)) then return box[1]; end
    return nil;
end

local function renderSettings()
    local run = require('vg.commands').Vg;
    local t, p = gVana.Toggles, gVana.Prefs;
    local s = gVana.Sets;
    local v;

    v = choice('Language', 'language', locale.Preference(), locale.Choices());
    if v ~= nil then run({ 'language', v }); end
    imgui.TextDisabled(locale.Format('Automatic detection: %s', locale.Detected()));
    imgui.TextWrapped('Display language changes labels and help, not your saved sets or commands.');
    imgui.Separator();

    vd.Eyebrow('Gear modes (the same as the /vg commands, which stay for your macros)');
    local dt = (t.DT == 'pdt') and 'pdt' or ((t.DT == 'mdt') and 'mdt' or (t.DT and 'all' or 'off'));
    v = choice('Damage taken', 'dt', dt, { { 'off', 'Off' }, { 'all', 'All (Dt)' }, { 'pdt', 'Physical (Dt_Physical)' }, { 'mdt', 'Magic (Dt_Magic)' } });
    if (v ~= nil) then run({ 'dt', v }); end
    if (s.Tp_Default ~= nil) then
        v = choice('Fighting set', 'tp', t.Tp, { { 'Default', 'Default' }, { 'Hybrid', 'Hybrid' }, { 'Acc', 'Accuracy' }, { 'Proc', 'Proc' } });
        if (v ~= nil) then run({ 'tp', string.lower(v) }); end
    end
    local idle = { { 'auto', 'Auto' }, { 'regen', 'Regen' } };
    if (s.Idle_Refresh ~= nil) then idle[#idle + 1] = { 'refresh', 'Refresh' }; end
    idle[#idle + 1] = { 'speed', 'Speed' };
    v = choice('Standing', 'idle', t.Idle or 'auto', idle);
    if (v ~= nil) then run({ 'idle', v }); end
    if (s.Nuke ~= nil) or (s.Enfeebling ~= nil) then
        v = choice('Nukes and enfeebles', 'nuke', t.Nuke or 'power', { { 'power', 'Damage' }, { 'acc', 'Accuracy' } });
        if (v ~= nil) then run({ 'nuke', v }); end
    end
    if (s.PetTP ~= nil) then
        v = choice('Your pet', 'pet', t.Pet or 'tp', { { 'tp', 'Fighting' }, { 'dt', 'Damage taken' }, { 'acc', 'Accuracy' } });
        if (v ~= nil) then run({ 'pet', v }); end
    end
    if (s.Burst ~= nil) then v = tick('Magic burst gear on nukes', 'burst', t.Burst); if (v ~= nil) then run({ 'burst', v and 'on' or 'off' }); end end
    v = tick('Movement gear over everything (kite)', 'kite', t.Kite); if (v ~= nil) then run({ 'kite', v and 'on' or 'off' }); end
    if (s.TH ~= nil) then v = tick('Treasure Hunter gear over everything', 'th', t.TH); if (v ~= nil) then run({ 'th', v and 'on' or 'off' }); end end
    if (s.Interim ~= nil) then v = tick('Staged casting (safety gear while casting, spell gear just before it lands)', 'stage', t.Stage); if (v ~= nil) then run({ 'stage', v and 'on' or 'off' }); end end

    imgui.Separator();
    vd.Eyebrow('How vanagear works for you');
    v = tick('Allow Main/Sub swaps from my profile (costs TP)', 'weapons', p.Weapons); if (v ~= nil) then run({ 'weapons', v and 'on' or 'off' }); end
    v = tick('Fill the slots your sets leave empty from the built sets', 'underlay', require('vg.core').Underlay()); if (v ~= nil) then run({ 'underlay', v and 'on' or 'off' }); end
    v = tick('Send your called trusts their sets', 'trusts', p.TrustGear ~= false); if (v ~= nil) then run({ 'trusts', v and 'on' or 'off' }); end
    v = tick('Motenten\'s F9-F12 keys, as GearSwap players have them', 'keys', p.MoteKeys); if (v ~= nil) then run({ 'keys', v and 'mote' or 'off' }); end
    imgui.Text('Wait after zoning in (seconds)');
    imgui.SameLine(190);
    waitBuffer[1] = p.ZoneWait or 10;
    imgui.PushItemWidth(90);
    if (imgui.InputInt('##vg_set_wait', waitBuffer)) and (waitBuffer[1] >= 0) then run({ 'wait', tostring(waitBuffer[1]) }); end
    imgui.PopItemWidth();

    imgui.Separator();
    vd.Eyebrow('Locked slots: kept as you have them, nothing equips them (saved for this job)');
    local locked = p.Locked[(gVana.Who and gVana.Who.JobName) or ''] or {};
    for i, slotName in ipairs(gData.Constants.EquipSlotNames) do
        if ((i - 1) % 4 ~= 0) then imgui.SameLine(((i - 1) % 4) * 150 + 8); end
        v = tick(slotName, 'lock' .. i, locked[i]);
        if (v ~= nil) then run({ v and 'lock' or 'unlock', slotName }); end
    end

    if (gProfile ~= nil) and (type(gProfile.State) == 'table') and (next(gProfile.State) ~= nil) then
        imgui.Separator();
        vd.Eyebrow('Your ' .. tostring(gProfile.VgJob or '') .. ' profile\'s own settings');
        local keys = {};
        for k in pairs(gProfile.State) do keys[#keys + 1] = k; end
        table.sort(keys);
        local choices = (type(gProfile.StateChoices) == 'table') and gProfile.StateChoices or {};
        for _, k in ipairs(keys) do
            local cur = gProfile.State[k];
            if (type(cur) == 'boolean') then
                v = tick(k, 'state_' .. k, cur);
                if (v ~= nil) then gProfile.State[k] = v; end
            elseif (choices[k] ~= nil) then
                local opts = {};
                for _, c in ipairs(choices[k]) do opts[#opts + 1] = { c, c }; end
                v = choice(k, 'state_' .. k, cur, opts);
                if (v ~= nil) then gProfile.State[k] = v; end
            end
        end
    end
end

-- A /vg command chosen in the side bar: what it does, and for one of yours its lines and Run.
local function renderCommand(entry)
    vd.Eyebrow(entry[1]);
    imgui.TextWrapped(entry[2]);
    local own = require('vg.mine').List()[string.match(entry[1], '^/vg (%S+)$') or ''];
    if (own ~= nil) then
        imgui.Separator();
        imgui.Text('Its lines:');
        for _, line in ipairs(own.lines) do imgui.BulletText(line); end
        if (vd.Button('Run it', nil, true)) then require('vg.mine').Run(string.match(entry[1], '^/vg (%S+)$')); end
        imgui.SameLine();
        imgui.TextDisabled('Change or delete it in the Your commands tab.');
    else
        imgui.Separator();
        imgui.TextDisabled('Type it in chat or put it in a macro. Words in <> are yours to fill in; a | means pick one.');
    end
end

window.RenderSettings = renderSettings;   -- for tests\commands_settings.py
window.RenderCommand = renderCommand;

-- The set names in the order the list shows them: everything else, then the spell sets.
local function sortedNames()
    local names, spells = {}, {};
    for name in pairs(gVana.Sets) do
        local ex = gVana.Explain[name];
        if (ex ~= nil) and (ex.spell ~= nil) then spells[#spells + 1] = name; else names[#names + 1] = name; end
    end
    table.sort(names);
    table.sort(spells);
    return names, spells;
end

-- The pieces the player carries that can go in this slot now, one per name. The support job's
-- gear is offered too: on Vanadreams the player's own sets are where it gets worn.
local function choices(slot)
    local bags = require('vg.bags');
    local list, seen = {}, {};
    for _, p in ipairs(gVana.Pieces) do
        if (not seen[p.Name]) and (gVana.Who ~= nil) and (bags.CanWear(p, slot, gVana.Who, true)) then
            seen[p.Name] = true;
            list[#list + 1] = p.Name;
        end
    end
    table.sort(list);
    return list;
end

local function pieceName(entry)
    if (type(entry) == 'table') then return entry.Name; end
    return entry;
end

-- The right pane: every slot of the selected set, each one a list to pick a piece from.
renderSlots = function(name)
    local mine = require('vg.mine');
    local ex = gVana.Explain[name];
    local set = gVana.Sets[name] or {};
    local own = gVana.Own[name] or {};
    local built = gVana.Built[name] or {};
    local yours = next(own) ~= nil;
    vd.Eyebrow(require('vg.mine').Label(name) .. ((ex and ex.weaponskill) and ('  (' .. ex.weaponskill .. ')') or '')
        .. ((ex and ex.spell) and '  (goes on as you cast it)' or ''));
    imgui.SameLine();
    imgui.TextDisabled(yours and (gVana.Built[name] and '  built, with your changes' or '  your own set') or '  built');
    -- lock the look to this set: the swaps still happen, nobody sees them
    local core = require('vg.core');
    local looked = { core.LookSet() == name };
    if (imgui.Checkbox('Lock my look to this set (the swaps stop showing)##vg_look_' .. name, looked)) then
        if (looked[1]) then
            if (core.SetLook(name)) then sayNote('Your look is locked to ' .. mine.Label(name) .. '. Untick, or /vg look off, to unlock.');
            else sayNote('Could not lock the look to ' .. mine.Label(name) .. '.'); end
        else
            core.SetLook(nil);
            sayNote('Look unlocked.');
        end
    end
    imgui.Separator();
    for slot = 1, 16 do
        local slotName = gData.Constants.EquipSlotNames[slot];
        local entry = pieceName(set[slotName]);
        if (slotName == 'Range') then
            local legacy = ((gVana.Prefs.WeaponPick or {})[gVana.Who and gVana.Who.JobName or ''] or {}).Range;
            if (legacy ~= nil) then
                imgui.TextWrapped('Previous every-set Range pick: ' .. legacy .. '. No longer forced; choose it below to save it in this set.');
            end
        end
        local ranks = ex and ex.ranks[slot];
        imgui.Text(string.format('%-6s', slotName));
        imgui.SameLine(80);
        if (slot <= 2) and (not gVana.Prefs.Weapons) then
            -- one weapon per slot for the job, worn in every set; nothing else ever changes it
            local core = require('vg.core');
            local picked = ((gVana.Prefs.WeaponPick or {})[gVana.Who and gVana.Who.JobName or ''] or {})[slotName];
            imgui.PushItemWidth(260);
            if (imgui.BeginCombo('##vg_slot_' .. slotName, picked or '(left alone)', ImGuiComboFlags_None)) then
                if (imgui.Selectable('Leave it alone##vg_wnone_' .. slotName, picked == nil)) then
                    core.SetWeaponPick(slotName, nil);
                end
                if (imgui.Selectable('Nothing (take it off)##vg_wrem_' .. slotName, picked == 'remove')) then
                    core.SetWeaponPick(slotName, 'remove');
                end
                for _, piece in ipairs(choices(slot)) do
                    if (imgui.Selectable(piece .. '##vg_wpick_' .. slotName, picked == piece)) then
                        core.SetWeaponPick(slotName, piece);
                    end
                end
                imgui.EndCombo();
            end
            imgui.PopItemWidth();
            imgui.SameLine();
            imgui.TextDisabled('every set');
        elseif (gVana.Held[slot] ~= nil) and (not own[slotName]) then
            imgui.TextDisabled(gVana.Held[slot]);
        else
            imgui.PushItemWidth(260);
            local preview = (entry or '(nothing)') .. (own[slotName] and '  *' or '');
            if (imgui.BeginCombo('##vg_slot_' .. slotName, preview, ImGuiComboFlags_None)) then
                local builtPiece = pieceName(built[slotName]);
                if (imgui.Selectable('Built: ' .. (builtPiece or 'nothing') .. '##vg_built_' .. slotName, not own[slotName])) then
                    mine.SetSlot(name, slotName, nil);
                end
                if (imgui.Selectable('Nothing (take it off)##vg_none_' .. slotName, own[slotName] and entry == 'remove')) then
                    mine.SetSlot(name, slotName, 'remove');
                end
                for _, piece in ipairs(choices(slot)) do
                    if (imgui.Selectable(piece .. '##vg_pick_' .. slotName, own[slotName] and entry == piece)) then
                        mine.SetSlot(name, slotName, piece);
                    end
                end
                imgui.EndCombo();
            end
            imgui.PopItemWidth();
            if (not own[slotName]) and (ranks ~= nil) and (ranks[1] ~= nil) then
                imgui.SameLine();
                vd.Status(string.format('+%.1f', ranks[1].gain), vd.Ok);
                if (imgui.IsItemHovered()) and (ranks[2] ~= nil) then
                    local lines = {};
                    for i = 2, math.min(4, #ranks) do
                        lines[#lines + 1] = string.format('%s  +%.1f', ranks[i].c.piece.Name, ranks[i].gain);
                    end
                    imgui.SetTooltip('Behind it:\n' .. table.concat(lines, '\n'));
                end
            end
        end
    end
    imgui.Separator();
    if (vd.Button('Put this set on me', nil, true)) then
        -- on once, from your profile; your profile's own swaps carry on from there
        require('vg.use').WearOnce(name, set);
        setNote = 'Put ' .. name .. ' on. Your profile swaps as usual from here (/vg wear ' .. name .. ' keeps it on).';
    end
    if (gVana.Built[name] ~= nil) then
        imgui.SameLine();
        if (vd.Button('Put the built set on me')) then
            require('vg.use').WearBuilt(name);
            setNote = 'Put the built ' .. name .. ' on, without your picks. Your sets are as they were.';
        end
    end
    imgui.SameLine();
    if (vd.Button("Save what I'm wearing to this set")) then
        mine.SaveSet(name);
        setNote = name .. ' now holds what you wear.';
    end
    if (mine.CanUndo(name)) then
        imgui.SameLine();
        if (vd.Button('Undo my last change')) then
            mine.Undo(name);
            setNote = 'Your last change to ' .. name .. ' is taken back.';
        end
    end
    if (yours) then
        imgui.SameLine();
        if (vd.Button(gVana.Built[name] and 'Clear my picks' or 'Delete this set')) then
            mine.RemoveSet(name);
            setNote = gVana.Built[name] and ('Your picks in ' .. name .. ' are cleared; the built set fills it. Undo brings them back.') or (name .. ' deleted.');
        end
    end
    imgui.TextDisabled('* = your pick. Name a set after an ability, weaponskill or spell and it goes on for it.');
    if (setNote ~= nil) then
        imgui.TextDisabled(setNote);
    end
end


-- A trust's tab: who it is and what it uses, its sets from your bags (Engaged, and a swap-in for
-- each kind of action; no Idle, a trust only needs its gear in a fight), every slot a list like yours. Nothing is worn: the trust gets the stats
-- of these pieces while you carry them, and the server swaps sets as the trust acts (vg\trusts.lua).
local trustNote = nil;
local trustSel = {};
local trustNewSet = { '' };
local onTrust = nil;
local overlaySets = { idle = false, engaged = false };

local function jobName(id)
    local s = AshitaCore:GetResourceManager():GetString('jobs.names', id);
    return (type(s) == 'string') and s:trimend('\x00') or '';
end

local function useLabel(trusts, use)
    if (use.Name == 'magic') then return 'other spells'; end
    if (use.Name == 'ability') then return 'other abilities'; end
    return trusts.Pretty(use.Name);
end

local function renderTrust(t)
    local trusts = require('vg.trusts');
    local bags = require('vg.bags');
    local who = trusts.Who(t);
    local i = trusts.Info(t);

    -- who the trust is, its jobs and what it uses
    vd.Eyebrow(t.Name);
    imgui.SameLine();
    imgui.TextDisabled('  stats only: your gear stays in your bags');
    local sub = (t.Sub ~= nil) and (t.Sub > 0) and string.format(', support job %s (%s)', jobName(t.Sub), t.SubName) or '';
    imgui.Text(string.format('Job: %s (%s) level %d%s', jobName(t.Job), t.JobName, t.Level or 0, sub));
    local sets = trusts.Sets(t);
    local hasSet = {};
    for _, s in ipairs(sets) do hasSet[s.Key] = true; end
    if (i == nil) then
        imgui.TextDisabled('Asking the server what ' .. t.Name .. ' uses...');
    elseif (#i.Uses == 0) then
        imgui.TextDisabled('Uses: only its weapon.');
    else
        -- what it uses: untick one and the trust stops using it (the server keeps the choice while it is out
        -- and every time you call it again)
        imgui.TextDisabled('Uses (untick one to stop ' .. t.Name .. ' using it):');
        for n, use in ipairs(i.Uses) do
            local on = { not trusts.IsOff(t, use) };
            if (imgui.Checkbox(useLabel(trusts, use) .. '##vg_use_' .. t.Id .. '_' .. use.Name, on)) then
                trusts.SetOff(t, use, not on[1]);
            end
            if (n % 3 ~= 0) and (n < #i.Uses) then imgui.SameLine(180 * (n % 3)); end
        end
    end
    imgui.Separator();

    local sel = trustSel[t.Name];
    local selected = nil;
    for _, s in ipairs(sets) do
        if (s.Name == sel) then selected = s; end
    end
    if (selected == nil) then
        selected = sets[2] or sets[1];
        trustSel[t.Name] = selected and selected.Name or nil;
    end

    -- the set list, and a set to add for each thing it uses that has none
    imgui.BeginChild('trust_sets', { 190, -60 }, ImGuiChildFlags_Borders);
    for _, s in ipairs(sets) do
        local picked = next(trusts.Own(t, s.Name)) ~= nil;
        if (vd.Selectable(s.Name .. (picked and '  *' or '') .. '##vg_ts_' .. s.Key, selected == s)) then
            trustSel[t.Name] = s.Name;
        end
    end
    if (i ~= nil) then
        local first = true;
        for _, use in ipairs(i.Uses) do
            local name = (use.Name == 'magic') and 'Magic' or (use.Name == 'ability') and 'Ability' or trusts.Pretty(use.Name);
            if (not hasSet[trusts.Key(name)]) then
                if (first) then
                    imgui.Separator();
                    imgui.TextDisabled('Add a set for:');
                    first = false;
                end
                hasSet[trusts.Key(name)] = true;
                if (imgui.Selectable('+ ' .. name .. '##vg_tu_' .. use.Name, false)) then
                    trustSel[t.Name] = trusts.AddSet(t, name);
                end
            end
        end
    end
    imgui.EndChild();
    imgui.SameLine();

    imgui.BeginChild('trust_set_slots', { 0, -60 }, ImGuiChildFlags_Borders);
    if (selected ~= nil) then
        local overlay = overlaySets[selected.Key] == nil;
        imgui.Text(selected.Name);
        imgui.SameLine();
        imgui.TextDisabled(selected.Built and ('  goes on for ' .. selected.For) or
            ('  goes on when ' .. t.Name .. ' uses ' .. selected.Name .. ' (or anything of that kind)'));
        if (overlay) then
            imgui.TextDisabled('Slots left empty keep the Engaged piece.');
        end
        local mine = trusts.Own(t, selected.Name);
        local set = trusts.Built(t)[selected.Name] or {};
        for slot = 1, 16 do
            local slotName = gData.Constants.EquipSlotNames[slot];
            local pick = mine[slotName];
            local builtPiece = set[slot] and set[slot].Name or nil;
            local shown = (pick == 'remove') and nil or (pick or builtPiece);
            imgui.Text(string.format('%-6s', slotName));
            imgui.SameLine(80);
            imgui.PushItemWidth(260);
            local empty = overlay and '(keeps Engaged)' or '(nothing)';
            local preview = (shown or empty) .. ((pick ~= nil) and '  *' or '');
            if (imgui.BeginCombo('##vg_t_' .. t.Name .. selected.Key .. slotName, preview, ImGuiComboFlags_None)) then
                if (selected.Built) and (imgui.Selectable('Built: ' .. (builtPiece or 'nothing') .. '##vg_tb_' .. slotName, pick == nil)) then
                    trusts.SetSlot(t, selected.Name, slotName, nil);
                end
                if (imgui.Selectable((overlay and 'Nothing: keep the Engaged piece' or 'Nothing') .. '##vg_tn_' .. slotName, pick == 'remove')) then
                    trusts.SetSlot(t, selected.Name, slotName, 'remove');
                end
                local seen = {};
                local list = {};
                for _, p in ipairs(gVana.Pieces or {}) do
                    if (not seen[p.Name]) and (bags.CanWear(p, slot, who, true)) then
                        seen[p.Name] = true;
                        list[#list + 1] = p.Name;
                    end
                end
                table.sort(list);
                for _, piece in ipairs(list) do
                    if (imgui.Selectable(piece .. '##vg_tp_' .. slotName, pick == piece)) then
                        trusts.SetSlot(t, selected.Name, slotName, piece);
                    end
                end
                imgui.EndCombo();
            end
            imgui.PopItemWidth();
        end
        if (next(mine) ~= nil) or (not selected.Built) then
            if (vd.Button(selected.Built and 'Back to the built set' or 'Delete this set')) then
                trusts.Reset(t, selected.Name);
                trustNote = selected.Built and (selected.Name .. ' is back to the built set.') or (selected.Name .. ' deleted.');
            end
        end
    end
    imgui.EndChild();
end

-- The bar under a trust's tab, in place of the one for your own sets.
local function renderTrustBar(t)
    local trusts = require('vg.trusts');
    imgui.PushItemWidth(160);
    imgui.InputText('##vg_t_new_set', trustNewSet, 64);
    imgui.PopItemWidth();
    imgui.SameLine();
    if (vd.Button('New set for ' .. t.Name)) then
        local name = trusts.AddSet(t, trustNewSet[1]);
        if (name ~= nil) then
            trustSel[t.Name] = name;
            trustNewSet[1] = '';
            trustNote = name .. ' goes on when ' .. t.Name .. ' uses it. Name it after a spell, weaponskill or ability.';
        end
    end
    imgui.SameLine();
    if (vd.Button('Send all again', nil, true)) then
        trusts.Send(t, true);
        trustNote = 'Sent every set. Changes go by themselves; the server keeps them for the next call.';
    end
    imgui.TextDisabled(trustNote or '* = your pick. Changes are sent by themselves; the trust keeps them for the next call.');
end

window.Render = function()
    if (not window.Open) then
        return;
    end
    isOpen[1] = true;
    imgui.SetNextWindowSize({ 720, 480 }, ImGuiCond_FirstUseEver);
    if (vd.Begin('vanagear', isOpen)) then
        local who = gVana.Who;
        if (who == nil) then
            imgui.Text('Waiting to build: sets are made once you have been in the zone for a few seconds.');
        else
            vd.Eyebrow(string.format('%s%d/%s%d', who.JobName, who.Level, who.SubName, who.SubLevel));
            imgui.SameLine();
            imgui.TextDisabled(string.format('  built from your inventory and wardrobes at %s', os.date('%H:%M', gVana.BuiltAt or os.time())));
            if (gProfile ~= nil) and (gProfile.FilePath ~= nil) then
                -- the profile file the sets are written into and worn from (vg\profile.lua)
                imgui.TextDisabled(string.format('Profile: %s  (your sets are written into it)',
                    string.match(gProfile.FilePath, 'profiles\\.*$') or gProfile.FilePath));
            end
            imgui.Separator();

            -- tabs: your sets, your commands, and one for every trust you have called
            local called = require('vg.trusts').Called();
            onTrust = nil;
            local tabs = imgui.BeginTabBar('##vg_tabs', ImGuiTabBarFlags_None);
            if (not tabs) or (imgui.BeginTabItem('Your sets##vg_tab_you')) then
                local names, spells = sortedNames();
                if (window.Selected == nil) or (gVana.Sets[window.Selected] == nil) then
                    window.Selected = gVana.Sets.Idle and 'Idle' or names[1] or spells[1];
                end

                imgui.BeginChild('sets', { 180, -60 }, ImGuiChildFlags_Borders);
                for _, name in ipairs(names) do
                    if (vd.Selectable(require('vg.mine').Label(name) .. '##vg_set_' .. name, window.Selected == name)) then
                        window.Selected = name;
                        window.Command = nil;
                    end
                end
                if (#spells > 0) then
                    imgui.Separator();
                    imgui.TextDisabled('Your spells');
                    for _, name in ipairs(spells) do
                        if (vd.Selectable(gVana.Explain[name].spell .. '##vg_set_' .. name, window.Selected == name)) then
                            window.Selected = name;
                            window.Command = nil;
                        end
                    end
                end
                -- every /vg command, the ones for this job and gear and yours (a new one shows here at once):
                -- they cannot be used if they are not known (Lee, 24 Oct 2026)
                local cmdLines = require('vg.commands').Lines();
                imgui.Separator();
                imgui.TextDisabled('Commands');
                for i, l in ipairs(cmdLines) do
                    if (vd.Selectable(l[1] .. '##vg_cmd_' .. i, window.Command == l[1])) then
                        window.Command = l[1];
                    end
                end
                imgui.EndChild();
                imgui.SameLine();

                imgui.BeginChild('slots', { 0, -60 }, ImGuiChildFlags_Borders);
                local chosen = nil;
                for _, l in ipairs(cmdLines) do if (l[1] == window.Command) then chosen = l; end end
                if (chosen ~= nil) then renderCommand(chosen); else window.Command = nil; renderSlots(window.Selected); end
                imgui.EndChild();
                if (tabs) then imgui.EndTabItem(); end
            end
            -- /vg mine opens straight on this tab
            local mineFlags = window.Mine and ImGuiTabItemFlags_SetSelected or ImGuiTabItemFlags_None;
            window.Mine = false;
            if (tabs) and (imgui.BeginTabItem('Your commands##vg_tab_mine', nil, mineFlags)) then
                imgui.BeginChild('mine', { 0, -60 }, ImGuiChildFlags_Borders);
                renderMine();
                imgui.EndChild();
                imgui.EndTabItem();
            end
            if (tabs) and (imgui.BeginTabItem('Settings##vg_tab_settings')) then
                imgui.BeginChild('settings', { 0, -60 }, ImGuiChildFlags_Borders);
                renderSettings();
                imgui.EndChild();
                imgui.EndTabItem();
            end
            if (tabs) and (imgui.BeginTabItem('How to##vg_tab_howto')) then
                imgui.BeginChild('howto', { 0, -60 }, ImGuiChildFlags_Borders);
                renderHowTo();
                imgui.EndChild();
                imgui.EndTabItem();
            end
            if (tabs) then
                for _, t in ipairs(called) do
                    -- only your own trusts: the server says which they are
                    if (require('vg.trusts').Ours(t)) and (imgui.BeginTabItem(t.Name .. '##vg_tab_' .. t.Id)) then
                        onTrust = t;
                        renderTrust(t);
                        imgui.EndTabItem();
                    end
                end
                imgui.EndTabBar();
            end
        end
        imgui.Separator();
        if (onTrust ~= nil) then
            renderTrustBar(onTrust);
            vd.End();
            if (not isOpen[1]) then window.Open = false; end
            return;
        end
        imgui.PushItemWidth(160);
        imgui.InputText('##vg_new_set', newSet, 64);
        imgui.PopItemWidth();
        imgui.SameLine();
        if (vd.Button('New set', nil, true)) then
            local mine = require('vg.mine');
            local name = mine.SetName(newSet[1]);
            if (name == '') then
                setNote = 'Type a name first.';
            elseif (gVana.Sets[name] ~= nil) and (not mine.Dressing) then
                window.Selected = name;
                setNote = name .. ' is already a set: change its slots on the right.';
            else
                local _, _, count = mine.SaveSet(name);
                window.Selected = name;
                newSet[1] = '';
                setNote = string.format('%s saved from what you wear (%d pieces). Change any slot on the right.', name, count or 0);
            end
        end
        imgui.SameLine();
        local mine = require('vg.mine');
        if (vd.Button(mine.Dressing and 'Swap again' or 'Dress')) then
            mine.Dress(not mine.Dressing);
            setNote = mine.Dressing and 'Swapping paused: put gear on, type a name and press New set (or /vg save <name>).' or nil;
        end
        imgui.SameLine();
        imgui.TextDisabled('/vg help lists the commands for this job.  vanagear is built on LuAshitacast by ThornyFFXI (MIT).');
    end
    vd.End();
    if (not isOpen[1]) then
        window.Open = false;
    end
end

return window;
